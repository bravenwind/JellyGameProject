using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SocketPractice
{
    /// <summary>
    /// [3단계] 다중 접속 + 호스트 판정 — 우리 게임 구조의 축소판.
    ///
    /// 여기서 연습하는 것이 곧 젤리팡 아일랜드의 네트워크 구조다:
    ///   · 호스트가 여러 클라의 연결을 동시에 관리          (Photon의 룸)
    ///   · 클라마다 수신 스레드 하나                        (Photon이 감춰줬던 부분)
    ///   · 클라는 '요청'만, 판정은 호스트가                 (마스터 클라이언트 권위)
    ///   · 결과를 전원에게 브로드캐스트                     (RpcTarget.All)
    ///   · 같은 젤리에 두 명이 달려들면 선착 1명만 승자      (_claimedJellies 선점 가드)
    ///
    /// 클라 명령:  /eat jelly_7   (젤리 먹었다고 주장)   /quit
    ///            그 외 입력은 채팅으로 전원에게 전달
    /// </summary>
    static class Step3MultiClient
    {
        const int Port = 7779;

        class ClientInfo
        {
            public int Id;
            public TcpClient Tcp;
            public NetworkStream Stream;
        }

        static readonly List<ClientInfo> _clients = new List<ClientInfo>();
        static readonly HashSet<string> _claimed = new HashSet<string>();   // 이미 먹힌 젤리
        static readonly object _lock = new object();
        static int _nextId = 1;

        // ─────────────────────────────────────────────
        // 호스트
        // ─────────────────────────────────────────────
        public static void RunHost()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();

            Console.WriteLine("[호스트] " + Port + "번 포트에서 대기 중.");
            Console.WriteLine("[호스트] 클라를 여러 개 띄워서 접속해 보세요. (Ctrl+C로 종료)");

            while (true)
            {
                // 접속을 계속 받아들인다 (Photon이 해주던 '룸 입장' 역할)
                TcpClient tcp = listener.AcceptTcpClient();

                ClientInfo info = new ClientInfo();
                info.Tcp = tcp;
                info.Stream = tcp.GetStream();

                lock (_lock)
                {
                    info.Id = _nextId++;
                    _clients.Add(info);
                }

                Console.WriteLine("[호스트] P" + info.Id + " 접속 (" + tcp.Client.RemoteEndPoint + "), 현재 " + ClientCount() + "명");

                // 클라 한 명당 수신 스레드 하나
                Thread t = new Thread(delegate () { ClientLoop(info); });
                t.IsBackground = true;
                t.Start();
            }
        }

        static void ClientLoop(ClientInfo me)
        {
            try
            {
                // 접속한 본인에게만 ID 알려주기 (Photon의 ActorNumber 발급에 해당)
                SendTo(me, "WELCOME 당신은 P" + me.Id + " 입니다.");
                Broadcast("JOIN P" + me.Id + " 님이 입장했습니다.");

                while (true)
                {
                    string msg = MessageIO.Receive(me.Stream);
                    Console.WriteLine("[호스트] P" + me.Id + " -> \"" + msg + "\"");
                    HandleMessage(me, msg);
                }
            }
            catch
            {
                // 연결 끊김
            }
            finally
            {
                lock (_lock) { _clients.Remove(me); }
                try { me.Stream.Close(); me.Tcp.Close(); } catch { }

                Console.WriteLine("[호스트] P" + me.Id + " 퇴장, 남은 " + ClientCount() + "명");
                Broadcast("LEAVE P" + me.Id + " 님이 나갔습니다.");
            }
        }

        /// <summary>호스트의 판정 로직. 클라는 '요청'만 하고 결정은 여기서 한다.</summary>
        static void HandleMessage(ClientInfo from, string msg)
        {
            if (msg.StartsWith("EAT "))
            {
                string jelly = msg.Substring(4).Trim();
                bool isWinner;

                // ★ 선착 1명만 승자 — 게임의 _claimedJellies 와 같은 원리.
                //   여러 스레드가 동시에 들어오므로 lock 이 반드시 필요하다.
                lock (_lock)
                {
                    isWinner = !_claimed.Contains(jelly);
                    if (isWinner) _claimed.Add(jelly);
                }

                if (isWinner)
                {
                    // 판정 결과는 전원에게 (RpcTarget.All)
                    Broadcast("RESULT " + jelly + " -> 승자 P" + from.Id);
                    Console.WriteLine("[호스트] 판정: " + jelly + " 는 P" + from.Id + " 의 것!");
                }
                else
                {
                    // 늦은 요청은 조용히 거절 (요청자에게만)
                    SendTo(from, "DENY " + jelly + " (이미 먹힌 젤리)");
                    Console.WriteLine("[호스트] 판정: P" + from.Id + " 의 " + jelly + " 요청 거절(중복)");
                }
                return;
            }

            // 그 외에는 채팅으로 취급
            Broadcast("CHAT P" + from.Id + ": " + msg);
        }

        static void Broadcast(string text)
        {
            List<ClientInfo> targets;
            lock (_lock) { targets = new List<ClientInfo>(_clients); }

            foreach (ClientInfo c in targets)
                SendTo(c, text);
        }

        static void SendTo(ClientInfo c, string text)
        {
            try
            {
                // 여러 스레드가 같은 스트림에 쓸 수 있으므로 잠금
                lock (c) { MessageIO.Send(c.Stream, text); }
            }
            catch { /* 끊긴 클라는 무시 (정리는 ClientLoop의 finally에서) */ }
        }

        static int ClientCount()
        {
            lock (_lock) { return _clients.Count; }
        }

        // ─────────────────────────────────────────────
        // 클라이언트
        // ─────────────────────────────────────────────
        public static void RunClient(string ip)
        {
            TcpClient client = new TcpClient();
            Console.WriteLine("[클라] " + ip + ":" + Port + " 접속 시도...");
            client.Connect(ip, Port);
            Console.WriteLine("[클라] 접속 성공!");
            Console.WriteLine("       /eat jelly_7  = 젤리 먹었다고 호스트에 요청");
            Console.WriteLine("       그 외 입력    = 채팅,   /quit = 종료");

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
                catch { Console.WriteLine("[클라] 호스트와 연결이 끊어졌습니다."); }
            });
            receiver.IsBackground = true;
            receiver.Start();

            while (true)
            {
                string line = Console.ReadLine();
                if (line == null || line == "/quit") break;
                if (line.Length == 0) continue;

                try
                {
                    if (line.StartsWith("/eat "))
                        MessageIO.Send(stream, "EAT " + line.Substring(5).Trim());
                    else
                        MessageIO.Send(stream, line);
                }
                catch
                {
                    Console.WriteLine("[클라] 전송 실패 - 연결이 끊긴 것 같습니다.");
                    break;
                }
            }

            stream.Close();
            client.Close();
        }
    }
}
