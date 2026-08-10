using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace JellyNet
{
    public static class NetUtil
    {
        public static List<string> GetLocalIPv4List()
        {
            List<string> result = new();

            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up)
                        continue;

                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        string ip = address.Address.ToString();

                        if (ip.StartsWith("169.254."))
                            continue;

                        result.Add($"{ip.PadRight(16)} ({adapter.Name})");
                    }
                }
            }
            catch
            {
            }

            if (result.Count == 0)
                result.Add("127.0.0.1");

            return result;
        }
    }
}
