﻿using cmonitor.client.report;
using cmonitor.config;
using cmonitor.libs;
using cmonitor.startup;
using common.libs;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using cmonitor.client.args;
using cmonitor.client.running;

namespace cmonitor.client
{
    public sealed class ClientStartup : IStartup
    {
        public void AddClient(ServiceCollection serviceCollection, Config config, Assembly[] assemblies)
        {
            serviceCollection.AddSingleton<RunningConfig>();

            serviceCollection.AddSingleton<SignInArgsTransfer>();

            serviceCollection.AddSingleton<ClientReportTransfer>();

            serviceCollection.AddSingleton<ClientSignInState>();
            serviceCollection.AddSingleton<ClientSignInTransfer>();

            //内存共享
            ShareMemory shareMemory = new ShareMemory(config.Data.Client.ShareMemoryKey, config.Data.Client.ShareMemoryCount, config.Data.Client.ShareMemorySize);
            serviceCollection.AddSingleton<ShareMemory>((a) => shareMemory);
        }

        public void UseClient(ServiceProvider serviceProvider, Config config, Assembly[] assemblies)
        {
            Logger.Instance.Info($"start client");
            Logger.Instance.Info($"server ip {config.Data.Client.ServerEP}");

            Logger.Instance.Info($"start client report transfer");
            ClientReportTransfer report = serviceProvider.GetService<ClientReportTransfer>();
            report.LoadPlugins(assemblies);

            Logger.Instance.Info($"start client share memory");
            ShareMemory shareMemory = serviceProvider.GetService<ShareMemory>();
            shareMemory.InitLocal();
            shareMemory.InitGlobal();
            shareMemory.StartLoop();

            Logger.Instance.Info($"start client signin transfer");
            ClientSignInTransfer clientTransfer = serviceProvider.GetService<ClientSignInTransfer>();
        }


        public void AddServer(ServiceCollection serviceCollection, Config config, Assembly[] assemblies)
        {

        }
        public void UseServer(ServiceProvider serviceProvider, Config config, Assembly[] assemblies)
        {
        }
    }
}
