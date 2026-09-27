using System;
using System.Collections.Generic;

namespace JellyNet
{
    public class SocketTransport : INetTransport
    {
        private readonly NetEvents events;

        private NetHost host;
        private NetClient client;

        public string LastError { get; private set; }

        private bool connectionLost;

        private readonly NetRouteTable routes;

        public SocketTransport(NetRouteTable routes, NetEvents events)
        {
            this.routes = routes;
            this.events = events;
        }

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

        public bool IsConnected { get { return host != null || client != null; } }

        public int PeerCount { get { return host != null ? host.PeerCount : 0; } }

        public bool AcceptingNewPeers
        {
            get { return host != null && host.AcceptingNewPeers; }
            set { if (host != null) host.AcceptingNewPeers = value; }
        }

        public bool StartHost(int port)
        {
            Shutdown();

            NetHost h = new NetHost();
            h.OnPeerJoined = RaisePeerJoined;
            h.OnPeerLeft = RaisePeerLeft;
            h.OnMessage = RaiseHostMessage;

            if (!h.Start(port))
            {
                LastError = "포트 " + port + " 를 열 수 없습니다. 다른 게임이 켜져 있는지 확인해주세요.";
                return false;
            }

            host = h;

            events.OpenSession();
            events.RaiseHostStarted();
            events.RaiseRoomEntered();
            return true;
        }

        public bool JoinHost(string ip, int port)
        {
            Shutdown();

            NetClient c = new NetClient();
            c.OnMessage = RaiseClientMessage;
            c.OnWelcome = events.RaiseRoomEntered;

            if (!c.Connect(ip, port))
            {
                LastError = ip + ":" + port + " 에 접속하지 못했습니다. 주소를 확인해주세요.";
                return false;
            }

            client = c;
            events.OpenSession();
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

            events.RaiseDisconnected();
        }

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

            events.RaiseConnectionLost();
        }

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
    }
}
