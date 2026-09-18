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
    ///
    /// 여기서는 호스트와 클라가 같은 MessageIO.Send / Receive 를 쓴다.
    /// 연결이 맺어진 뒤에는 양쪽이 대칭이기 때문에 이렇게 공용으로 쓸 수 있다.
    /// </summary>
    static class Step2Framing
    {
        const int Port = 7778;

        public static void RunHost()
        {
            TcpListener listener = NetHelper.StartHostListener(Port);
            if (listener == null) return;
            Console.WriteLine("[호스트] (프레이밍 적용 버전)");

            TcpClient clientConn = listener.AcceptTcpClient();   // 접속해 온 클라와의 연결
            Console.WriteLine("[호스트] 접속됨: " + clientConn.Client.RemoteEndPoint);

            NetworkStream stream = clientConn.GetStream();
            int count = 0;

            try
            {
                while (true)
                {
                    // 한 번 호출 = 정확히 메시지 하나 (길이를 먼저 읽어 그만큼만 잘라내므로)
                    string msg = MessageIO.Receive(stream);
                    count++;
                    Console.WriteLine("[호스트] 메시지 #" + count + ": \"" + msg + "\"");

                    MessageIO.Send(stream, "echo: " + msg);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[호스트] 수신 종료 - " + e.Message);
            }

            stream.Close();
            clientConn.Close();
            listener.Stop();
        }

        public static void RunClient(string ip)
        {
            TcpClient hostConn = NetHelper.ConnectToHost(ip, Port);
            if (hostConn == null) return;

            Console.WriteLine("       /burst = 3개 연속 전송,   /quit = 종료");

            NetworkStream stream = hostConn.GetStream();

            // 키보드 입력(메인)과 수신을 동시에 기다려야 하므로 스레드를 하나 더 쓴다
            Thread receiver = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        string msg = MessageIO.Receive(stream);
                        Console.WriteLine("  <- " + msg);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("[클라] 수신 중단: " + e.Message);
                }
                Console.WriteLine("[클라] 수신 종료");
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
            hostConn.Close();
        }
    }
}
