using System;
using System.Net;
using System.Net.Sockets;

namespace SocketPractice
{
    /// <summary>
    /// 접속/대기 시작을 감싸서, 실패했을 때 예외 덤프 대신
    /// "무엇을 확인해야 하는지" 안내를 보여주는 도우미.
    ///
    /// 소켓 오류는 원인이 몇 가지로 정해져 있어서, 코드만 보고도 진단이 가능하다.
    ///   ConnectionRefused  = 주소엔 닿았는데 그 포트에 듣는 프로그램이 없음
    ///   TimedOut           = 주소에 아예 닿지 못함(IP/방화벽/네트워크)
    ///   AddressAlreadyInUse= 그 포트를 이미 다른 프로그램이 쓰는 중
    /// </summary>
    static class NetHelper
    {
        /// <summary>호스트에 접속. 실패하면 안내를 출력하고 null 을 돌려준다.</summary>
        public static TcpClient ConnectToHost(string ip, int port)
        {
            TcpClient conn = new TcpClient();
            Console.WriteLine("[클라] " + ip + ":" + port + " 로 접속 시도...");

            try
            {
                conn.Connect(ip, port);
                Console.WriteLine("[클라] 접속 성공!");
                return conn;
            }
            catch (SocketException e)
            {
                Console.WriteLine();
                Console.WriteLine("[클라] 접속 실패: " + e.Message);
                Console.WriteLine();

                if (e.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    Console.WriteLine("  * '연결 거부' = 그 주소에는 닿았지만, " + port + "번 포트에서");
                    Console.WriteLine("    듣고 있는 프로그램이 없다는 뜻입니다. 아래를 확인하세요.");
                    Console.WriteLine();
                    Console.WriteLine("    1) 다른 터미널에서 호스트를 먼저 켰나요?");
                    Console.WriteLine("       dotnet run -- step1-host");
                    Console.WriteLine("    2) 같은 PC에서 테스트 중이라면 IP는 127.0.0.1 을 쓰세요.");
                    Console.WriteLine("    3) 다른 기기라면 호스트 PC에서 ipconfig 를 치고");
                    Console.WriteLine("       'IPv4 주소'를 쓰세요. (기본 게이트웨이가 아닙니다!)");
                }
                else if (e.SocketErrorCode == SocketError.TimedOut
                      || e.SocketErrorCode == SocketError.HostUnreachable
                      || e.SocketErrorCode == SocketError.NetworkUnreachable)
                {
                    Console.WriteLine("  * '시간 초과 / 도달 불가' = 그 주소에 아예 닿지 못했습니다.");
                    Console.WriteLine();
                    Console.WriteLine("    1) IP가 맞나요? (호스트 PC의 ipconfig -> IPv4 주소)");
                    Console.WriteLine("    2) 호스트 PC 방화벽에서 이 프로그램을 허용했나요?");
                    Console.WriteLine("    3) 두 기기가 같은 와이파이인가요? 학교/카페 와이파이는");
                    Console.WriteLine("       기기끼리 통신이 막혀 있는 경우가 많습니다(AP 격리).");
                }
                else
                {
                    Console.WriteLine("  * IP -> 포트 -> 방화벽 -> 같은 네트워크인지 순서로 확인해 보세요.");
                }

                Console.WriteLine();
                try { conn.Close(); } catch { }
                return null;
            }
        }

        /// <summary>호스트 대기 시작. 실패하면 안내를 출력하고 null 을 돌려준다.</summary>
        public static TcpListener StartHostListener(int port)
        {
            // IPAddress.Any = 이 PC의 모든 네트워크 카드에서 수신 (LAN 접속 받으려면 Any)
            TcpListener listener = new TcpListener(IPAddress.Any, port);

            try
            {
                listener.Start();
                Console.WriteLine("[호스트] " + port + "번 포트에서 대기 중... 클라 접속을 기다립니다.");
                return listener;
            }
            catch (SocketException e)
            {
                Console.WriteLine();
                Console.WriteLine("[호스트] 대기 시작 실패: " + e.Message);
                Console.WriteLine();

                if (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    Console.WriteLine("  * " + port + "번 포트를 이미 다른 프로그램이 쓰고 있습니다.");
                    Console.WriteLine("    이전에 켠 호스트 창이 아직 살아있지 않은지 확인하거나,");
                    Console.WriteLine("    코드의 Port 값을 다른 번호로 바꿔보세요.");
                }

                Console.WriteLine();
                return null;
            }
        }
    }
}
