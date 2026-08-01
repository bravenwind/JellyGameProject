using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace JellyNet
{
    /// <summary>
    /// 호스트(서버 겸 플레이어) 측 로직.
    /// Photon의 '룸 + 마스터 클라이언트' 역할을 대신한다.
    ///
    /// 스레드를 쓰지 않으므로 Poll()을 매 프레임 호출해야 한다.
    /// </summary>
    public class NetHost
    {
        public class Peer
        {
            public int Id;
            public FramedConnection Conn;

            /// <summary>
            /// 이 peer 전용 수신 콜백. 접속할 때 딱 한 번 만들어 재사용한다.
            ///
            /// 매 프레임 `Poll(r => HandleMessage(p, r))` 처럼 그 자리에서 람다를 쓰면,
            /// 컴파일러가 p를 담을 객체(클로저)를 프레임마다·peer마다 새로 만든다.
            /// 8명 60fps면 초당 480개가 생겼다 버려지므로, 여기 보관해 할당을 0으로 만든다.
            /// </summary>
            public Action<NetReader> OnMsg;
        }

        TcpListener _listener;
        readonly List<Peer> _peers = new List<Peer>();
        readonly NetWriter _w = new NetWriter();
        int _nextId = 2;              // 1번은 호스트 자신으로 예약

        public const int HostId = 1;

        public bool Running { get; private set; }
        public int PeerCount { get { return _peers.Count; } }
        public IReadOnlyList<Peer> Peers { get { return _peers; } }

        public Action<string> OnLog;

        /// <summary>여기서 처리하지 않는 메시지를 상위로 넘긴다. (보낸 사람, 타입, 리더)</summary>
        public Action<Peer, MsgType, NetReader> OnMessage;

        /// <summary>새 클라가 접속했을 때. 게임 오브젝트 스폰 등을 여기서 한다.</summary>
        public Action<Peer> OnPeerJoined;

        /// <summary>클라가 나갔을 때. 그 사람 소유 오브젝트 정리 등.</summary>
        public Action<Peer> OnPeerLeft;

        // ─────────────────────────────────────────────
        public bool Start(int port)
        {
            try
            {
                // IPAddress.Any = 모든 네트워크 카드에서 수신(LAN 접속을 받으려면 필수)
                _listener = new TcpListener(IPAddress.Any, port);

                // ★ 포트를 다시 쓸 수 있게 한다.
                //
                //   TCP는 리스너를 닫아도 커널이 그 포트를 잠깐(TIME_WAIT) 붙잡고 있다.
                //   에디터에서 방을 열었다 닫았다 반복하면 그 사이에 걸려
                //   "포트가 이미 사용 중"이 뜬다. 실제로는 아무도 안 쓰는데도.
                //
                //   ExclusiveAddressUse를 끄고 ReuseAddress를 켜면 재바인딩이 허용된다.
                //   (같은 포트를 진짜로 다른 프로세스가 듣고 있으면 여전히 실패한다)
                _listener.ExclusiveAddressUse = false;
                _listener.Server.SetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                _listener.Start();
                Running = true;
                Log("호스트 시작 — 포트 " + port);
                return true;
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    Log("포트 " + port + "가 이미 사용 중입니다. 이전 인스턴스가 살아있는지 확인하세요.");
                else
                    Log("호스트 시작 실패: " + e.Message);
                Running = false;
                return false;
            }
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;

            for (int i = 0; i < _peers.Count; i++) _peers[i].Conn.Close();
            _peers.Clear();

            try { _listener.Stop(); } catch { }
            Log("호스트 종료");
        }

        // ─────────────────────────────────────────────
        //  매 프레임
        // ─────────────────────────────────────────────
        public void Poll()
        {
            if (!Running) return;

            // ── 1) 새 접속 받기 ──
            //    Pending() = "대기 중인 접속이 있나?" → 있을 때만 Accept하므로 멈추지 않는다
            while (_listener.Pending())
            {
                TcpClient tcp = _listener.AcceptTcpClient();
                Peer p = new Peer();
                p.Id = _nextId++;
                p.Conn = new FramedConnection(tcp);
                p.OnMsg = r => HandleMessage(p, r);   // 접속 시 한 번만 생성 → 이후 재사용
                _peers.Add(p);

                Log("P" + p.Id + " 접속 (" + tcp.Client.RemoteEndPoint + ") — 현재 " + _peers.Count + "명");

                // 본인에게 ID 통보 (Photon의 ActorNumber 발급에 해당)
                _w.Begin(MsgType.Welcome);
                _w.WriteInt(p.Id);
                _w.End();
                SendTo(p, _w);

                // 전원에게 입장 알림
                _w.Begin(MsgType.PlayerJoined);
                _w.WriteInt(p.Id);
                _w.End();
                Broadcast(_w);

                // 게임 계층에 알림 (오브젝트 스폰, 기존 상태 전송 등)
                if (OnPeerJoined != null) OnPeerJoined(p);
            }

            // ── 2) 각 클라의 수신 처리 ──
            //    뒤에서부터 도는 이유: 중간에 리스트에서 제거해도 인덱스가 흐트러지지 않는다
            for (int i = _peers.Count - 1; i >= 0; i--)
            {
                Peer p = _peers[i];
                p.Conn.Poll(p.OnMsg);      // 미리 만들어둔 콜백 재사용 (매 프레임 할당 없음)

                if (!p.Conn.Alive)
                {
                    _peers.RemoveAt(i);
                    Log("P" + p.Id + " 퇴장 (" + p.Conn.LastError + ") — 남은 " + _peers.Count + "명");

                    // 게임 계층이 먼저 정리(소유 오브젝트 파괴 등)
                    if (OnPeerLeft != null) OnPeerLeft(p);

                    _w.Begin(MsgType.PlayerLeft);
                    _w.WriteInt(p.Id);
                    _w.End();
                    Broadcast(_w);
                }
            }
        }

        void HandleMessage(Peer from, NetReader r)
        {
            MsgType type = r.ReadMsgType();

            switch (type)
            {
                case MsgType.Ping:
                    {
                        // 받은 순번을 그대로 되돌려준다 → 클라가 왕복 시간을 계산
                        int seq = r.ReadInt();
                        _w.Begin(MsgType.Pong);
                        _w.WriteInt(seq);
                        _w.End();
                        from.Conn.Send(_w);
                        break;
                    }

                case MsgType.Chat:
                    {
                        string text = r.ReadString();
                        Log("P" + from.Id + ": " + text);

                        // 전원에게 중계 (RpcTarget.All 에 해당)
                        _w.Begin(MsgType.Chat);
                        _w.WriteInt(from.Id);
                        _w.WriteString(text);
                        _w.End();
                        Broadcast(_w);
                        break;
                    }

                default:
                    // 게임 계층(NetWorld 등)이 처리할 메시지들
                    if (OnMessage != null) OnMessage(from, type, r);
                    break;
            }
        }

        // ─────────────────────────────────────────────
        public void SendChat(string text)
        {
            _w.Begin(MsgType.Chat);        // ← 재사용하는 _w 사용
            _w.WriteInt(HostId);
            _w.WriteString(text);
            _w.End();
            Broadcast(_w);
        }

        public void Broadcast(NetWriter w)
        {
            // ★ 반드시 매개변수 w를 보낼 것. 필드 _w를 보내면 호스트가 마지막으로 쓴
            //   엉뚱한 메시지가 나간다(B-2에서는 항상 _w == w 라 증상이 없었다).
            for (int i = 0; i < _peers.Count; i++)
            {
                SendTo(_peers[i], w);
            }
        }

        public void SendTo(Peer p, NetWriter w)
        {
            p.Conn.Send(w);
        }

        /// <summary>플레이어 번호로 접속을 찾는다. 호스트 자신(1번)은 소켓이 없으므로 null.</summary>
        public Peer FindPeer(int playerId)
        {
            for (int i = 0; i < _peers.Count; i++)
                if (_peers[i].Id == playerId) return _peers[i];
            return null;
        }

        /// <summary>보낸 사람만 빼고 전원에게. 위치 중계에 쓴다(자기 위치를 되돌려받을 필요 없음).</summary>
        public void BroadcastExcept(Peer except, NetWriter w)
        {
            for (int i = 0; i < _peers.Count; i++)
                if (_peers[i] != except) _peers[i].Conn.Send(w);
        }

        void Log(string msg)
        {
            if (OnLog != null) OnLog("[호스트] " + msg);
        }
    }
}
