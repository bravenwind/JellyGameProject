using System;
using System.Collections.Generic;

namespace JellyNet
{
    /// <summary>
    /// 순수 C# TCP 소켓(NetHost/NetClient)으로 INetTransport 를 구현한다.
    ///
    /// ★ 소켓보다 오래 산다
    ///   이 객체는 NetManager 와 수명이 같고, 한 판이 끝나도 죽지 않는다.
    ///   Shutdown 은 소켓만 닫고 라우팅 표는 남긴다. 라우팅 표를 소켓과 같이 버리면
    ///   로비처럼 "접속하기 전에 RouteClient 를 걸어두는" 코드가 전부 무효가 된다
    ///   (LanLobby 는 Start 에서 LoadGameScene 을 등록하고 한참 뒤에 참가한다).
    /// </summary>
    public class LanTransport : INetTransport
    {
        private NetHost host;
        private NetClient client;

        /// <summary>로그 한 줄을 어디에 남길지. NetManager 가 꽂아준다.</summary>
        public Action<string> OnLog;

        /// <summary>
        /// 그냥 로그가 아니라 콘솔에 빨갛게 떠야 하는 것. 라우팅 중복 등록처럼
        /// 조용히 넘어가면 다음 판에서야 증상이 나타나는 실수가 여기로 온다.
        /// (이 클래스는 유니티에 의존하지 않으므로 Debug.LogError 를 직접 부르지 않는다)
        /// </summary>
        public Action<string> OnError;

        public event Action<int> OnPeerJoined;
        public event Action<int> OnPeerLeft;
        public event Action OnHostStarted;
        public event Action OnDisconnected;
        public event Action OnConnectionLost;

        /// <summary>StartHost/JoinHost 가 실패한 이유. 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public string LastError { get; private set; }

        public bool ConnectionLost { get; private set; }

        // ─────────────────────────────────────────────────────────
        //  상태
        // ─────────────────────────────────────────────────────────

        public int MyId
        {
            get
            {
                if (host != null)
                    return NetHost.HOST_ID;
                return client != null ? client.MyId : 0;
            }
        }

        public bool IsHost { get { return host != null; } }

        //소켓이 살아 있는지가 아니라 세션이 서 있는지다. 끊긴 뒤에도 Shutdown 전까지는 참
        public bool IsConnected { get { return host != null || client != null; } }

        public int PeerCount { get { return host != null ? host.PeerCount : 0; } }

        public bool AcceptingNewPeers
        {
            get { return host != null && host.AcceptingNewPeers; }
            set { if (host != null) host.AcceptingNewPeers = value; }
        }

        // ─────────────────────────────────────────────────────────
        //  세션 열고 닫기 (LAN 전용 — 인터페이스 밖이다)
        // ─────────────────────────────────────────────────────────

        /// <summary>방을 연다. 실패하면 false 이고 LastError 에 이유가 담긴다.</summary>
        public bool StartHost(int port)
        {
            Shutdown();

            NetHost h = new NetHost();
            h.OnLog = Log;
            h.OnPeerJoined = RaisePeerJoined;
            h.OnPeerLeft = RaisePeerLeft;
            h.OnMessage = RaiseHostMessage;

            if (!h.Start(port))
            {
                LastError = "포트 " + port + " 를 열 수 없습니다. 다른 게임이 켜져 있는지 확인해주세요.";
                return false;
            }

            //host 대입은 Start 성공 뒤에 한다. 먼저 넣으면 실패한 세션이 IsHost 로 보인다
            host = h;

            Log("== 호스트 모드 ==  내 번호: P" + NetHost.HOST_ID);
            foreach (string ip in NetUtil.GetLocalIPv4List())
                Log("  다른 기기에서 접속: " + ip + ":" + port);

            OnHostStarted?.Invoke();
            return true;
        }

        /// <summary>방에 붙는다. 실패하면 false — IP 오타·방이 닫힘 등.</summary>
        public bool JoinHost(string ip, int port)
        {
            Shutdown();

            NetClient c = new NetClient();
            c.OnLog = Log;
            c.OnMessage = RaiseClientMessage;

            if (!c.Connect(ip, port))
            {
                LastError = ip + ":" + port + " 에 접속하지 못했습니다. 주소를 확인해주세요.";
                return false;
            }

            client = c;
            Log("== 참가 모드 ==");
            return true;
        }

        public void Shutdown()
        {
            bool wasConnected = (host != null || client != null);

            ConnectionLost = false;

            if (host != null)
            {
                host.Stop();
                host = null;
            }
            if (client != null)
            {
                client.Disconnect();
                client = null;
            }

            //호스트·클라를 비운 뒤에 알린다. 구독자가 이 자리에서 상태를 다시 물어보기 때문에
            //(로비의 취소 처리가 그렇다) 아직 세션이 서 있는 것처럼 보이면 Shutdown 이 다시 불린다
            if (wasConnected)
                OnDisconnected?.Invoke();
        }

        //소켓은 메시지 수에 한도가 없다. 묶으면 한 프레임 지연만 손해다
        public bool PrefersBatchedUpdates { get { return false; } }

        public void Poll()
        {
            if (host != null)
                host.Poll();

            if (client == null)
                return;

            client.Poll();

            if (ConnectionLost || client.Connected)
                return;

            ConnectionLost = true;
            Log("호스트와의 연결이 끊어졌습니다.");

            OnConnectionLost?.Invoke();
        }

        // ─────────────────────────────────────────────────────────
        //  보내기
        // ─────────────────────────────────────────────────────────
        //
        //호스트가 아닌데 Broadcast 를 부르는 건 호출부의 실수지만, 예전처럼 NullReference 로
        //터뜨리는 대신 조용히 버린다. 판이 끝나 소켓을 닫은 뒤에도 커튼 애니메이션 동안
        //게임 씬의 Update 가 계속 도는 구간이 판마다 반드시 지나가기 때문이다.

        public void Broadcast(NetWriter w)
        {
            if (host != null)
                host.Broadcast(w);
        }

        public void BroadcastExcept(int exceptPeerId, NetWriter w)
        {
            if (host != null)
                host.BroadcastExcept(exceptPeerId, w);
        }

        public void SendTo(int peerId, NetWriter w)
        {
            if (host != null)
                host.SendTo(peerId, w);
        }

        public void SendToHost(NetWriter w)
        {
            if (client != null)
                client.Send(w);
        }

        // ─────────────────────────────────────────────────────────
        //  메시지 라우팅 테이블
        // ─────────────────────────────────────────────────────────
        //
        // ★ 왜 이벤트 브로드캐스트로는 부족한가
        //   멀티캐스트 이벤트는 구독자 전원이 같은 메시지를 순서대로 받는다. 문제가 셋 있었다.
        //
        //     1. MsgType 하나를 추가하면 NetWorld·AbsorbMode·LanGameFlow 중
        //        어디 switch 에 넣을지 매번 골라야 하고, 아무 데도 안 넣어도 조용하다.
        //     2. 두 구독자가 같은 타입을 읽으면 NetReader 를 공유하므로 두 번째는
        //        위치가 밀린 채 쓰레기를 읽는다. 예외도 안 난다.
        //     3. 어떤 타입을 누가 담당하는지 코드 어디에도 안 적혀 있다.
        //
        //   타입당 주인을 하나로 못 박으면 셋 다 사라진다. 중복 등록은 그 자리에서
        //   에러로 잡히고, 주인 없는 타입은 로그에 남는다.
        private readonly Dictionary<MsgType, Action<int, NetReader>> hostRoutes
            = new Dictionary<MsgType, Action<int, NetReader>>();

        private readonly Dictionary<MsgType, Action<NetReader>> clientRoutes
            = new Dictionary<MsgType, Action<NetReader>>();

        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            if (handler == null)
                return;

            if (hostRoutes.ContainsKey(type))
            {
                LogError("호스트 메시지 " + type + " 의 주인이 이미 있습니다. "
                    + "한 타입은 한 곳에서만 처리해야 합니다.");
                return;
            }

            hostRoutes[type] = handler;
        }

        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            if (handler == null)
                return;

            if (clientRoutes.ContainsKey(type))
            {
                LogError("클라 메시지 " + type + " 의 주인이 이미 있습니다. "
                    + "한 타입은 한 곳에서만 처리해야 합니다.");
                return;
            }

            clientRoutes[type] = handler;
        }

        //씬을 나갈 때 반드시 풀어야 한다. 안 그러면 파괴된 오브젝트의 메서드가 남아
        //다음 판에서 "주인이 이미 있습니다" 에러가 뜬다
        public void UnrouteHost(MsgType type)
        {
            hostRoutes.Remove(type);
        }

        public void UnrouteClient(MsgType type)
        {
            clientRoutes.Remove(type);
        }

        private void RaiseHostMessage(int peerId, MsgType type, NetReader reader)
        {
            Action<int, NetReader> route;
            if (hostRoutes.TryGetValue(type, out route))
            {
                route(peerId, reader);
                return;
            }

            Log("처리되지 않은 호스트 메시지: " + type);
        }

        private void RaiseClientMessage(MsgType type, NetReader reader)
        {
            Action<NetReader> route;
            if (clientRoutes.TryGetValue(type, out route))
            {
                route(reader);
                return;
            }

            Log("처리되지 않은 클라 메시지: " + type);
        }

        private void RaisePeerJoined(int peerId)
        {
            OnPeerJoined?.Invoke(peerId);
        }

        private void RaisePeerLeft(int peerId)
        {
            OnPeerLeft?.Invoke(peerId);
        }

        private void Log(string msg)
        {
            OnLog?.Invoke(msg);
        }

        private void LogError(string msg)
        {
            if (OnError != null)
                OnError(msg);
            else
                OnLog?.Invoke("[오류] " + msg);
        }
    }
}
