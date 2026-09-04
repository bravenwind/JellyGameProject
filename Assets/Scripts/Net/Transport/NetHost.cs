using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace JellyNet
{
    public class NetHost
    {
        //★ 예전엔 public이었다
        //  상위 코드가 Peer를 그대로 받아 다녔지만 실제로 읽는 건 Id 하나뿐이었고,
        //  그 값은 언제나 OwnerId와 같았다. 소켓 한 개를 들고 다니는 타입이 API 밖으로
        //  새어나가면 전송 방식을 바꿀 때(온라인 모드) 상위 코드를 전부 고쳐야 한다.
        //  바깥은 int peerId 로만 말하고, 소켓과의 대응은 이 클래스 안에 가둔다.
        private class Peer
        {
            public int Id;
            public FramedConnection Conn;

            public Action<NetReader> OnMsg;
        }

        private TcpListener listener;
        private readonly List<Peer> peers = new List<Peer>();
        private readonly NetWriter writer = new NetWriter();
        private int nextId = 2;

        public const int HOST_ID = 1;

        public bool Running { get; private set; }
        public int PeerCount { get { return peers.Count; } }
        public Action<string> OnLog;

        public Action<int, MsgType, NetReader> OnMessage;

        public Action<int> OnPeerJoined;

        public Action<int> OnPeerLeft;

        //LAN은 한 판의 인원이 로비에서 확정된다. 호스트가 게임 씬에 들어가는 순간 문을 닫는다.
        //열어두면 늦게 붙은 쪽은 캐릭터가 없어 화면만 멈춘 채로 남는다 — 거절이 더 친절하다.
        public bool AcceptingNewPeers = true;

        public bool Start(int port)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);

                listener.ExclusiveAddressUse = false;
                listener.Server.SetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                listener.Start();
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
            if (!Running)
                return;
            Running = false;

            for (int i = 0; i < peers.Count; i++) peers[i].Conn.Kill();
            peers.Clear();

            try { listener.Stop(); } catch { }
            Log("호스트 종료");
        }

        public void Poll()
        {
            if (!Running)
                return;

            while (listener.Pending())
            {
                TcpClient tcp = listener.AcceptTcpClient();

                if (!AcceptingNewPeers)
                {
                    Log("게임이 이미 시작돼 접속을 거절했습니다 (" + tcp.Client.RemoteEndPoint + ")");
                    tcp.Close();
                    continue;
                }

                Peer p = new Peer();
                p.Id = nextId++;
                p.Conn = new FramedConnection(tcp);
                p.OnMsg = r => HandleMessage(p, r);
                peers.Add(p);

                Log("P" + p.Id + " 접속 (" + tcp.Client.RemoteEndPoint + ") — 현재 " + peers.Count + "명");

                writer.Begin(MsgType.Welcome);
                writer.WriteInt(p.Id);
                writer.End();
                SendToPeer(p, writer);

                writer.Begin(MsgType.PlayerJoined);
                writer.WriteInt(p.Id);
                writer.End();
                Broadcast(writer);

                OnPeerJoined?.Invoke(p.Id);
            }

            for (int i = peers.Count - 1; i >= 0; i--)
            {
                Peer p = peers[i];
                p.Conn.Poll(p.OnMsg);

                if (!p.Conn.Alive)
                {
                    peers.RemoveAt(i);
                    Log("P" + p.Id + " 퇴장 (" + p.Conn.LastError + ") — 남은 " + peers.Count + "명");

                    OnPeerLeft?.Invoke(p.Id);

                    writer.Begin(MsgType.PlayerLeft);
                    writer.WriteInt(p.Id);
                    writer.End();
                    Broadcast(writer);
                }
            }
        }

        private void HandleMessage(Peer from, NetReader r)
        {
            MsgType type = r.ReadMsgType();

            OnMessage?.Invoke(from.Id, type, r);
        }

        public void Broadcast(NetWriter w)
        {
            for (int i = 0; i < peers.Count; i++)
            {
                SendToPeer(peers[i], w);
            }
        }

        /// <summary>
        /// 그 번호의 접속자에게만 보낸다. 없으면 조용히 버린다.
        ///
        /// ★ 예전엔 FindPeer로 Peer를 찾아 넘겨야 했다
        ///   호출부마다 "찾고 → null이면 return → 보낸다"를 되풀이했고,
        ///   그 사이에 나가버린 사람은 어차피 보낼 곳이 없다. 없는 번호로 보내는 건
        ///   오류가 아니라 정상적인 경우라서 여기서 흡수한다.
        /// </summary>
        public void SendTo(int peerId, NetWriter w)
        {
            Peer p = FindPeer(peerId);
            if (p == null)
                return;
            SendToPeer(p, w);
        }

        public void BroadcastExcept(int exceptPeerId, NetWriter w)
        {
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].Id != exceptPeerId)
                    SendToPeer(peers[i], w);
        }

        private void SendToPeer(Peer p, NetWriter w)
        {
            p.Conn.Send(w);
        }

        private Peer FindPeer(int playerId)
        {
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].Id == playerId)
                    return peers[i];
            return null;
        }

        private void Log(string msg)
        {
            OnLog?.Invoke("[호스트] " + msg);
        }
    }
}
