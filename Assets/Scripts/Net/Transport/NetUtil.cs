using System.Collections.Generic;
using System.Net;
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

        public static List<IPEndPoint> GetBroadcastTargets(int port)
        {
            List<IPEndPoint> targets = new();
            HashSet<string> seen = new();

            AddTarget(targets, seen, IPAddress.Broadcast, port);

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

                        if (address.Address.ToString().StartsWith("169.254."))
                            continue;

                        try
                        {
                            AddTarget(targets, seen, DirectedBroadcast(address.Address, address.IPv4Mask), port);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return targets;
        }

        private static void AddTarget(List<IPEndPoint> targets, HashSet<string> seen, IPAddress addr, int port)
        {
            if (addr == null)
                return;

            if (seen.Add(addr.ToString()))
                targets.Add(new IPEndPoint(addr, port));
        }


        private static IPAddress DirectedBroadcast(IPAddress ip, IPAddress mask)
        {
            if (ip == null || mask == null)
                return null;

            byte[] a = ip.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();

            if (a.Length != 4 || m.Length != 4)
                return null;

            byte[] result = new byte[4];
            for (int i = 0; i < 4; i++)
                result[i] = (byte)(a[i] | (byte)~m[i]);

            return new IPAddress(result);
        }

        /// <summary>
        /// 화면에 보여줄 "내 IP". 랜카드가 여럿이면 통신에 쓰일 가능성이 높은 쪽을 고른다.
        ///
        /// Dns.GetHostAddresses는 어댑터 순서대로 돌려주기만 해서, VPN·VirtualBox가
        /// 앞에 오면 접속되지 않는 주소를 안내하게 된다. 여기서는 사설 대역
        /// (192.168 / 10 / 172.16~31)을 우선하고, 그것도 없으면 살아 있는 첫 IPv4를 쓴다.
        /// </summary>
        public static string GetPrimaryIPv4()
        {
            string fallback = null;

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

                        if (IsPrivateLan(address.Address))
                            return ip;

                        fallback ??= ip;
                    }
                }
            }
            catch
            {
            }

            return fallback ?? "127.0.0.1";
        }

        private static bool IsPrivateLan(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            if (b.Length != 4)
                return false;

            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;

            return false;
        }
    }
}
