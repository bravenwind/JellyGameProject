using System;

namespace JellyNet
{
    public interface INetTransport
    {
        // 호스트는 1, 클라이언트는 2부터. 연결 전에는 0
        int MyId { get; }

        bool IsHost { get; }

        bool IsConnected { get; }

        int PeerCount { get; }

        bool AcceptingNewPeers { get; set; }

        void Broadcast(NetWriter w);

        void BroadcastExcept(int exceptPeerId, NetWriter w);

        void SendTo(int peerId, NetWriter w);

        void SendToHost(NetWriter w);

        bool PrefersBatchedUpdates { get; }

        void Poll();

        void Shutdown();
    }
}
