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
    ///
    /// ── 헷갈리기 쉬운 두 가지 ──
    ///
    /// ① TcpClient 는 "클라이언트"가 아니라 '연결 하나(전화 수화기)'를 뜻한다.
    ///    그래서 호스트 쪽에도 TcpClient 가 있다. 아래처럼 "누구와의 연결인지"로
    ///    이름을 붙여두면 헷갈리지 않는다.
    ///      호스트: clientConn = 접속해 온 클라와의 연결
    ///      클라  : hostConn   = 내가 호스트와 맺은 연결
    ///    연결이 맺어진 뒤에는 양쪽이 완전히 대칭이다(둘 다 Read/Write).
    ///
    /// ② 스레드 개수는 "동시에 기다려야 하는 독립적인 대상의 수"로 정해진다.
    ///    이 호스트는 에코(응답만 하는) 서버라서 흐름이 Read→Write→Read→Write 로
    ///    고정돼 있다. 기다릴 대상이 '수신' 하나뿐이므로 스레드 1개로 충분하다.
    ///    반면 클라는 '키보드 입력'과 '호스트 수신'을 동시에 기다려야 하므로 2개가 필요하다.
    ///    (3단계 호스트는 클라가 여러 명이므로 클라마다 수신 스레드를 하나씩 둔다)
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

            // 누군가 접속할 때까지 여기서 멈춰서 기다린다(블로킹).
            // 리턴값 = "접속해 온 그 클라와의 연결" (내가 아니라 상대방을 가리키는 손잡이)
            TcpClient clientConn = listener.AcceptTcpClient();

            // TcpClient.Client 는 그 안에 들어있는 진짜 소켓. 상세 정보는 여기서 꺼낸다.
            Console.WriteLine("[호스트] 접속됨: " + clientConn.Client.RemoteEndPoint);

            NetworkStream stream = clientConn.GetStream();
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

                // 버퍼는 재사용하므로 뒤쪽에 지난 데이터가 남아있다 -> 반드시 n 만큼만 해석
                string text = Encoding.UTF8.GetString(buffer, 0, n);
                Console.WriteLine("[호스트] 받음(" + n + "바이트): \"" + text + "\"");

                // 클라와 똑같은 Send 헬퍼를 쓴다(같은 일을 두 방식으로 쓰지 않도록)
                Send(stream, "echo: " + text);
            }

            stream.Close();
            clientConn.Close();
            listener.Stop();
        }

        // ─────────────────────────────────────────────
        // 클라이언트 (전화를 거는 쪽)
        // ─────────────────────────────────────────────
        public static void RunClient(string ip)
        {
            TcpClient hostConn = new TcpClient();     // 호스트와 맺을 연결
            Console.WriteLine("[클라] " + ip + ":" + Port + " 로 접속 시도...");
            hostConn.Connect(ip, Port);              // 연결 성립(3-way handshake는 TCP가 알아서)
            Console.WriteLine("[클라] 접속 성공! 메시지를 입력하세요.");
            Console.WriteLine("       /burst = 3개 빠르게 연속 전송(뭉침 실험)   /quit = 종료");

            NetworkStream stream = hostConn.GetStream();

            // 받는 일은 별도 스레드에서.
            // 메인 스레드는 Console.ReadLine() 에서 멈춰 있으므로, 그 동안 Read를 부를 수 없다.
            Thread receiver = new Thread(() =>
            {
                byte[] buffer = new byte[1024];      // 이 스레드 전용 그릇
                try
                {
                    while (true)
                    {
                        int n = stream.Read(buffer, 0, buffer.Length);
                        if (n <= 0) break;           // 상대가 곱게 끊음(정상 종료)
                        Console.WriteLine("  <- " + Encoding.UTF8.GetString(buffer, 0, n));
                    }
                }
                catch (Exception e)
                {
                    // 강제 종료·네트워크 끊김처럼 갑작스런 경우엔 예외가 날아온다.
                    // 빈 catch 로 삼키면 진짜 버그도 숨으므로 이유를 찍어준다.
                    Console.WriteLine("[클라] 수신 중단: " + e.Message);
                }
                Console.WriteLine("[클라] 수신 종료");
            });
            receiver.IsBackground = true;    // 배경 일꾼 -> 메인이 끝나면 같이 정리된다
            receiver.Start();                // ★ 이 순간부터 위 코드가 실행된다

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
            hostConn.Close();
        }

        /// <summary>문자열을 바이트로 바꿔 그대로 전송(프레이밍 없음). 호스트·클라 공용.</summary>
        static void Send(NetworkStream stream, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            stream.Write(data, 0, data.Length);
        }
    }
}
