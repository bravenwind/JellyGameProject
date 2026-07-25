using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SocketPractice
{
    /// <summary>
    /// [1단계] 가장 단순한 TCP 에코 서버/클라이언트.
    ///
    /// 여기서는 일부러 "프레이밍"을 하지 않는다.
    /// 목적: 보낸 메시지 3개가 받는 쪽에서 한 덩어리로 뭉쳐 오는 걸 직접 보는 것.
    /// 클라에서 /burst 를 입력하면 3개를 빠르게 연속 전송한다.
    /// </summary>
    static class Step1Echo
    {
        const int Port = 7777;

        // ─────────────────────────────────────────────
        // 호스트 (전화를 기다리는 쪽)
        // ─────────────────────────────────────────────
        public static void RunHost()
        {
            // IPAddress.Any = 이 PC의 모든 네트워크 카드에서 수신 (LAN 접속 받으려면 Any)
            TcpListener listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Console.WriteLine("[호스트] " + Port + "번 포트에서 대기 중... 클라 접속을 기다립니다.");

            // 누군가 접속할 때까지 여기서 멈춰서 기다린다(블로킹)
            TcpClient client = listener.AcceptTcpClient();
            Console.WriteLine("[호스트] 접속됨: " + client.Client.RemoteEndPoint);

            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            while (true)
            {
                // ★ 핵심: Read는 "몇 바이트가 올지" 보장하지 않는다.
                //    보낸 메시지 여러 개가 한 번에 오기도, 하나가 쪼개져 오기도 한다.
                int n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                {
                    Console.WriteLine("[호스트] 클라가 연결을 끊었습니다.");
                    break;
                }

                string text = Encoding.UTF8.GetString(buffer, 0, n);
                Console.WriteLine("[호스트] 받음(" + n + "바이트): \"" + text + "\"");

                byte[] reply = Encoding.UTF8.GetBytes("echo: " + text);
                stream.Write(reply, 0, reply.Length);
            }

            stream.Close();
            client.Close();
            listener.Stop();
        }

        // ─────────────────────────────────────────────
        // 클라이언트 (전화를 거는 쪽)
        // ─────────────────────────────────────────────
        public static void RunClient(string ip)
        {
            TcpClient client = new TcpClient();
            Console.WriteLine("[클라] " + ip + ":" + Port + " 로 접속 시도...");
            client.Connect(ip, Port);           // 연결 성립(3-way handshake는 TCP가 알아서)
            Console.WriteLine("[클라] 접속 성공! 메시지를 입력하세요.");
            Console.WriteLine("       /burst = 3개 빠르게 연속 전송(뭉침 실험)   /quit = 종료");

            NetworkStream stream = client.GetStream();

            // 받는 일은 별도 스레드에서 (안 그러면 입력 대기 중에 수신을 못 함)
            Thread receiver = new Thread(delegate ()
            {
                byte[] buffer = new byte[1024];
                try
                {
                    while (true)
                    {
                        int n = stream.Read(buffer, 0, buffer.Length);
                        if (n <= 0) break;
                        Console.WriteLine("  <- " + Encoding.UTF8.GetString(buffer, 0, n));
                    }
                }
                catch { }
                Console.WriteLine("[클라] 수신 종료");
            });
            receiver.IsBackground = true;    // 메인이 끝나면 같이 정리되도록
            receiver.Start();

            while (true)
            {
                string line = Console.ReadLine();
                if (line == null || line == "/quit") break;

                if (line == "/burst")
                {
                    // 3개를 아주 빠르게 연속 전송 -> 호스트 쪽에서 뭉쳐 보일 가능성이 높다
                    Send(stream, "HELLO");
                    Send(stream, "WORLD");
                    Send(stream, "BYE");
                    Console.WriteLine("  -> 3개 전송함. 호스트 콘솔을 확인하세요!");
                    continue;
                }

                Send(stream, line);
            }

            stream.Close();
            client.Close();
        }

        static void Send(NetworkStream stream, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            stream.Write(data, 0, data.Length);
        }
    }
}
