﻿using common.libs;
using common.libs.extends;
using System.Net;
using System.Text.Json.Serialization;

namespace cmonitor.config
{
    public sealed partial class ConfigInfo
    {
        public ConfigClientInfo Client { get; set; } = new ConfigClientInfo();
    }

    public sealed partial class ConfigClientInfo
    {
        private string server = new IPEndPoint(IPAddress.Loopback, 1802).ToString();
        public string Server
        {
            get => server; set
            {
                server = value;
                if (string.IsNullOrWhiteSpace(server) == false)
                {
                    string[] arr = server.Split(':');
                    int port = arr.Length == 2 ? int.Parse(arr[1]) : 1802;
                    IPAddress ip = NetworkHelper.GetDomainIp(arr[0]);
                    ServerEP = new IPEndPoint(ip, port);
                }
            }
        }
        [JsonIgnore]
        public IPEndPoint ServerEP { get; set; } = new IPEndPoint(IPAddress.Loopback, 1802);


        private string name = Dns.GetHostName().SubStr(0, 12);
        public string Name
        {
            get => name; set
            {
                name = value.SubStr(0, 12);
            }
        }

        public string ShareMemoryKey { get; set; } = "cmonitor/share";
        public int ShareMemoryCount { get; set; } = 100;
        public int ShareMemorySize { get; set; } = 1024;

    }
}
