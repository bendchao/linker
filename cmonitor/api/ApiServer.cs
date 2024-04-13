﻿using cmonitor.api.websocket;
using cmonitor.config;
using common.libs;
using common.libs.extends;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace cmonitor.api
{
    /// <summary>
    /// 前段接口服务
    /// </summary>
    public sealed class ApiServer : IApiServer
    {
        private readonly Dictionary<string, PluginPathCacheInfo> plugins = new();
        private readonly ConcurrentDictionary<uint, ConnectionTimeInfo> connectionTimes = new();
        public uint OnlineNum = 0;

        private readonly ServiceProvider serviceProvider;
        private WebSocketServer server;
        private readonly Config config;

        public ApiServer(ServiceProvider serviceProvider, Config config)
        {
            this.serviceProvider = serviceProvider;
            this.config = config;
        }

        /// <summary>
        /// 加载插件
        /// </summary>
        /// <param name="assemblys"></param>
        public void LoadPlugins(Assembly[] assemblys)
        {
            Type voidType = typeof(void);

            IEnumerable<Type> types = assemblys.SelectMany(c => c.GetTypes()).Where(c => c.GetInterfaces().Contains(typeof(IApiController)));
            if (config.Data.Common.PluginNames.Length > 0)
            {
                types = types.Where(c => config.Data.Common.PluginNames.Any(d => c.FullName.Contains(d)));
            }

            foreach (Type item in types)
            {
                object obj = serviceProvider.GetService(item);
                if(obj == null)
                {
                    continue;
                }
                Logger.Instance.Warning($"load server api:{item.Name}");

                string path = item.Name.Replace("ApiController", "");
                foreach (MethodInfo method in item.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    string key = $"{path}/{method.Name}".ToLower();
                    if (!plugins.ContainsKey(key))
                    {
                        bool istask = method.ReturnType.GetProperty("IsCompleted") != null && method.ReturnType.GetMethod("GetAwaiter") != null;
                        bool isTaskResult = method.ReturnType.GetProperty("Result") != null;
                        plugins.TryAdd(key, new PluginPathCacheInfo
                        {
                            IsVoid = method.ReturnType == voidType,
                            Method = method,
                            Target = obj,
                            IsTask = istask,
                            IsTaskResult = isTaskResult
                        });
                    }
                }
            }
        }
        /// <summary>
        /// 开启websockt
        /// </summary>
        public void Websocket()
        {
            server = new WebSocketServer();
            try
            {
                server.Start(System.Net.IPAddress.Any, config.Data.Server.ApiPort);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error(ex);
            }
            server.OnOpen = (connection) =>
            {
                Interlocked.Increment(ref OnlineNum);
                connectionTimes.TryAdd(connection.Id, new ConnectionTimeInfo());
            };
            server.OnDisConnectd = (connection) =>
            {
                Interlocked.Decrement(ref OnlineNum);
                if (OnlineNum < 0) Interlocked.Exchange(ref OnlineNum, 0);
                connectionTimes.TryRemove(connection.Id, out _);
            };
            server.OnMessage = (connection, frame, message) =>
            {
                if (connectionTimes.TryGetValue(connection.Id, out ConnectionTimeInfo timeInfo))
                {
                    timeInfo.DateTime = DateTime.Now;
                }
                var req = message.DeJson<ApiControllerRequestInfo>();
                req.Connection = connection;
                OnMessage(req).ContinueWith((result) =>
                {
                    var resp = result.Result.ToJson().ToBytes();
                    connection.SendFrameText(resp);
                });
            };
        }

        /// <summary>
        /// 收到消息
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task<ApiControllerResponseInfo> OnMessage(ApiControllerRequestInfo model)
        {
            model.Path = model.Path.ToLower();
            if (plugins.TryGetValue(model.Path, out PluginPathCacheInfo plugin) == false)
            {
                return new ApiControllerResponseInfo
                {
                    Content = "not exists this path",
                    RequestId = model.RequestId,
                    Path = model.Path,
                    Code = ApiControllerResponseCodes.NotFound
                };
            }

            try
            {
                ApiControllerParamsInfo param = new ApiControllerParamsInfo
                {
                    RequestId = model.RequestId,
                    Content = model.Content,
                    Connection = model.Connection
                };
                dynamic resultAsync = plugin.Method.Invoke(plugin.Target, new object[] { param });
                object resultObject = null;
                if (plugin.IsVoid == false)
                {
                    if (plugin.IsTask)
                    {
                        await resultAsync.ConfigureAwait(false);
                        if (plugin.IsTaskResult)
                        {
                            resultObject = resultAsync.Result;
                        }
                    }
                    else
                    {
                        resultObject = resultAsync;
                    }
                }
                return new ApiControllerResponseInfo
                {
                    Code = param.Code,
                    Content = param.Code != ApiControllerResponseCodes.Error ? resultObject : param.ErrorMessage,
                    RequestId = model.RequestId,
                    Path = model.Path,
                };
            }
            catch (Exception ex)
            {
                Logger.Instance.Error(ex);
                return new ApiControllerResponseInfo
                {
                    Content = ex.Message,
                    RequestId = model.RequestId,
                    Path = model.Path,
                    Code = ApiControllerResponseCodes.Error
                };
            }
        }

        public void Notify(string path, object content)
        {
            if (server.Connections.Any())
            {
                try
                {
                    byte[] bytes = JsonSerializer.Serialize(new ApiControllerResponseInfo
                    {
                        Code = ApiControllerResponseCodes.Success,
                        Content = content,
                        Path = path,
                        RequestId = 0
                    }).ToBytes();

                    foreach (WebsocketConnection connection in server.Connections)
                    {
                        if (connection.Connected && connectionTimes.TryGetValue(connection.Id, out ConnectionTimeInfo timeInfo) && (DateTime.Now - timeInfo.DateTime).TotalMilliseconds < 1000)
                        {
                            try
                            {
                                connection.SendFrameText(bytes);
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.Error(ex);
                }
            }
        }

        public void Notify(string path, string name, Memory<byte> content)
        {
            if (server.Connections.Any())
            {
                try
                {
                    Memory<byte> headMemory = JsonSerializer.Serialize(new ApiControllerResponseInfo
                    {
                        Code = ApiControllerResponseCodes.Success,
                        Content = name,
                        Path = path,
                        RequestId = 0
                    }).ToBytes();

                    int length = 4 + headMemory.Length + content.Length;
                    byte[] result = ArrayPool<byte>.Shared.Rent(length);

                    int index = 0;
                    headMemory.Length.ToBytes(result);
                    index += 4;
                    headMemory.CopyTo(result.AsMemory(index));
                    index += headMemory.Length;
                    content.CopyTo(result.AsMemory(index));
                    index += content.Length;

                    foreach (WebsocketConnection connection in server.Connections)
                    {
                        if (connection.Connected && connectionTimes.TryGetValue(connection.Id, out ConnectionTimeInfo timeInfo) && (DateTime.Now - timeInfo.DateTime).TotalMilliseconds < 1000)
                        {
                            try
                            {
                                connection.SendFrameBinary(result.AsMemory(0, length));
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                    ArrayPool<byte>.Shared.Return(result);
                }
                catch (Exception)
                {
                    //Logger.Instance.Error(ex);
                }
            }
        }

        public void Notify(string path, object content, WebsocketConnection connection)
        {
            try
            {
                if (connection.Connected == false) return;

                byte[] bytes = JsonSerializer.Serialize(new ApiControllerResponseInfo
                {
                    Code = ApiControllerResponseCodes.Success,
                    Content = content,
                    Path = path,
                    RequestId = 0
                }).ToBytes();

                try
                {
                    connection.SendFrameText(bytes);
                }
                catch (Exception)
                {
                }
            }
            catch (Exception)
            {
                //Logger.Instance.Error(ex);
            }
        }
    }

    public sealed class ConnectionTimeInfo
    {
        public DateTime DateTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 前段接口缓存
    /// </summary>
    public struct PluginPathCacheInfo
    {
        /// <summary>
        /// 对象
        /// </summary>
        public object Target { get; set; }
        /// <summary>
        /// 方法
        /// </summary>
        public MethodInfo Method { get; set; }
        /// <summary>
        /// 是否void
        /// </summary>
        public bool IsVoid { get; set; }
        /// <summary>
        /// 是否task
        /// </summary>
        public bool IsTask { get; set; }
        /// <summary>
        /// 是否task result
        /// </summary>
        public bool IsTaskResult { get; set; }
    }
}
