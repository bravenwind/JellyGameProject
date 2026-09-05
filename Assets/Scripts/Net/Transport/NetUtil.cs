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

        // GetPrimaryIPv4 / IsPrivateLan 을 지웠다.
        // 매칭 패널의 주소 칸이 "192.168.0.5 : 7777" 대신 방 이름을 띄우게 되면서
        // 마지막 호출부가 사라졌다. 온라인에는 보여줄 IP 가 없고, 로컬에서도
        // 사람이 알아야 하는 건 "누구 방에 있는가"이지 주소가 아니다.
        // (IsPrivateLan 은 GetPrimaryIPv4 만 쓰던 도우미라 같이 사라진다)
    }
}
