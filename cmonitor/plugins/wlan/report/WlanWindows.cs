﻿using ManagedNativeWifi;

namespace cmonitor.plugins.wlan.report
{
    public class WlanWindows : IWlan
    {
        public List<string> WlanEnums()
        {
            return NativeWifi.EnumerateAvailableNetworks().Where(c => string.IsNullOrWhiteSpace(c.ProfileName) == false).Select(c => c.ProfileName).ToList();
        }

        public async Task<bool> WlanConnect(string name)
        {
            try
            {
                var targetInterface = NativeWifi.EnumerateInterfaces()
                 .FirstOrDefault(x =>
                 {
                     var radioSet = NativeWifi.GetInterfaceRadio(x.Id)?.RadioSets.FirstOrDefault();
                     if (radioSet is null)
                         return false;

                     if (!radioSet.HardwareOn.GetValueOrDefault()) // Hardware radio state is off.
                         return false;

                     return (radioSet.SoftwareOn == false); // Software radio state is off.
                 });

                if (targetInterface != null)
                {
                    NativeWifi.TurnOnInterfaceRadio(targetInterface.Id);
                }
            }
            catch (Exception)
            {
            }

            var wifi = NativeWifi.EnumerateAvailableNetworks().FirstOrDefault(c => c.ProfileName == name);
            if (wifi == null)
            {
                return false;
            }
            return await NativeWifi.ConnectNetworkAsync(wifi.Interface.Id, wifi.ProfileName, wifi.BssType, TimeSpan.FromSeconds(5));
        }

        public bool Connected()
        {
            //using Ping ping = new Ping();
            ////var replay = ping.Send("www.baidu.com", 5000);
            //return replay.Status == IPStatus.Success;
            return common.libs.winapis.Wininet.InternetGetConnectedState(out int desc, 0);
        }
    }
}
