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
    public class SocketTransport : INetTransport
    {
        public SocketTransport(NetRouteTable routes, NetEvents events)
        {
            this.routes = routes;
            this.events = events;
        }

        //일어난 일을 알리는 게시판. NetManager 가 하나 만들어 모두에게 꽂아준다
        private readonly NetEvents events;

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


        /// <summary>StartHost/JoinHost 가 실패한 이유. 화면에 그대로 띄울 수 있는 문장이다.</summary>
        public string LastError { get; private set; }

        //이번 접속에서 끊김을 이미 알렸는가. Poll 이 매 프레임 같은 로그를 찍지 않게 한다
        private bool connectionLost;

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

            //호스트는 남의 승인을 기다릴 게 없다. 포트가 열린 순간 세션이 서고 방에 들어간 것이다
            events.OpenSession();
            events.RaiseHostStarted();
            events.RaiseRoomEntered();
            return true;
        }

        /// <summary>방에 붙는다. 실패하면 false — IP 오타·방이 닫힘 등.</summary>
        public bool JoinHost(string ip, int port)
        {
            Shutdown();

            NetClient c = new NetClient();
            c.OnLog = Log;
            c.OnMessage = RaiseClientMessage;
            //클라가 호스트에게서 자기 번호를 받은 순간이 방에 들어간 순간이다. LAN 은 접속(TCP)과
            //번호 배정이 따로라, 접속만으로 들어갔다고 하면 번호가 0인 채로 대기 화면이 뜬다
            c.OnWelcome = events.RaiseRoomEntered;

            if (!c.Connect(ip, port))
            {
                LastError = ip + ":" + port + " 에 접속하지 못했습니다. 주소를 확인해주세요.";
                return false;
            }

            client = c;
            events.OpenSession();
            Log("== 참가 모드 ==");
            return true;
        }

        public void Shutdown()
        {
            connectionLost = false;

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
            //세션이 없었으면 게시판이 알아서 조용히 넘긴다
            events.RaiseDisconnected();
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

            if (connectionLost || client.Connected)
                return;

            connectionLost = true;
            Log("호스트와의 연결이 끊어졌습니다.");

            events.RaiseConnectionLost();
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
        // 표는 NetManager 가 하나 만들어 두 전송에 같이 꽂아준다.
        // 왜 전송마다 갖지 않는지는 NetRouteTable 의 머리말에 적었다.
        private readonly NetRouteTable routes;

        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            routes.RouteHost(type, handler);
        }

        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            routes.RouteClient(type, handler);
        }

        public void UnrouteHost(MsgType type) { routes.UnrouteHost(type); }

        public void UnrouteClient(MsgType type) { routes.UnrouteClient(type); }

        private void RaiseHostMessage(int peerId, MsgType type, NetReader reader)
        {
            routes.DispatchHost(peerId, type, reader);
        }

        private void RaiseClientMessage(MsgType type, NetReader reader)
        {
            routes.DispatchClient(type, reader);
        }

        private void RaisePeerJoined(int peerId)
        {
            events.RaisePeerJoined(peerId);
        }

        private void RaisePeerLeft(int peerId)
        {
            events.RaisePeerLeft(peerId);
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
