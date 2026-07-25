using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SocketPractice
{
    /// <summary>
    /// [2단계] 1단계와 똑같지만 MessageIO(길이 프리픽스)를 쓴다.
    ///
    /// 1단계에서 /burst 하면 "HELLOWORLDBYE"로 뭉쳐 보였다.
    /// 여기서 /burst 하면 항상 HELLO / WORLD / BYE 세 개로 정확히 나뉜다.
    /// 이 차이를 눈으로 확인하는 것이 2단계의 목표.
    /// </summary>
    static class Step2Framing
    {
        const int Port = 7778;

        public static void RunHost()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Console.WriteLine("[호스트] " + Port + "번 포트 대기 중 (프레이밍 적용)...");

            TcpClient client = listener.AcceptTcpClient();
            Console.WriteLine("[호스트] 접속됨: " + client.Client.RemoteEndPoint);

            NetworkStream stream = client.GetStream();
            int count = 0;

            try
            {
                while (true)
                {
                    // 한 번 호출 = 정확히 메시지 하나
                    string msg = MessageIO.Receive(stream);
                    count++;
                    Console.WriteLine("[호스트] 메시지 #" + count + ": \"" + msg + "\"");

                    MessageIO.Send(stream, "echo: " + msg);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[호스트] 종료 - " + e.Message);
            }

            stream.Close();
            client.Close();
            listener.Stop();
        }

        public static void RunClient(string ip)
        {
            TcpClient client = new TcpClient();
            Console.WriteLine("[클라] " + ip + ":" + Port + " 접속 시도...");
            client.Connect(ip, Port);
            Console.WriteLine("[클라] 접속 성공! (/burst = 3개 연속 전송, /quit = 종료)");

            NetworkStream stream = client.GetStream();

            Thread receiver = new Thread(delegate ()
            {
                try
                {
                    while (true)
                    {
                        string msg = MessageIO.Receive(stream);
                        Console.WriteLine("  <- " + msg);
                    }
                }
                catch { Console.WriteLine("[클라] 수신 종료"); }
            });
            receiver.IsBackground = true;
            receiver.Start();

            while (true)
            {
                string line = Console.ReadLine();
                if (line == null || line == "/quit") break;

                if (line == "/burst")
                {
                    MessageIO.Send(stream, "HELLO");
                    MessageIO.Send(stream, "WORLD");
                    MessageIO.Send(stream, "BYE");
                    Console.WriteLine("  -> 3개 전송함. 호스트에서 #1 #2 #3으로 나뉘어 보일 겁니다.");
                    continue;
                }

                MessageIO.Send(stream, line);
            }

            stream.Close();
            client.Close();
        }
    }
}
