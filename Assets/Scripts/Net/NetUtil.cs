using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace JellyNet
{
    public static class NetUtil
    {
        /// <summary>
        /// 이 PC의 LAN IPv4 주소 목록. 호스트가 "이 주소로 접속하세요"를 띄우는 데 쓴다.
        ///
        /// 주의: 가상 어댑터(VMware/VirtualBox/Hyper-V)나 꺼진 어댑터까지 나오면 헷갈리므로
        ///       '연결된 유선/무선'만 골라낸다. 그래도 여러 개 나올 수 있다.
        ///       (기본 게이트웨이 주소 — 보통 끝이 .1 — 는 공유기이지 내 PC가 아니다)
        /// </summary>
        public static List<string> GetLocalIPv4List()
        {
            List<string> result = new List<string>();

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 꺼져 있거나 루프백 어댑터만 제외한다.
                    // (하마치·ZeroTier·Tailscale 같은 가상 어댑터도 보여줘야 하므로
                    //  종류로 걸러내지 않고, 대신 어댑터 이름을 함께 표시한다)
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;  // IPv4만

                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;   // 자동 할당 실패 주소(연결 안 된 상태)

                        // 어댑터 이름이 붙어 있으면 "어느 IP를 알려줘야 하는지" 바로 알 수 있다.
                        //   예)  192.168.0.5    (Wi-Fi)        ← 같은 와이파이 테스트용
                        //        25.34.12.9     (Hamachi)      ← 원격(VPN) 테스트용
                        result.Add(ip.PadRight(16) + " (" + ni.Name + ")");
                    }
                }
            }
            catch { /* 플랫폼에 따라 조회가 막힐 수 있음 — 그때는 목록 없이 진행 */ }

            if (result.Count == 0) result.Add("127.0.0.1");
            return result;
        }
    }
}
