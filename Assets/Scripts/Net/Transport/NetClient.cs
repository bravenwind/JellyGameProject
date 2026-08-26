using System;
using System.Net.Sockets;

namespace JellyNet
{
    public class NetClient
    {
        private FramedConnection conn;

        public int MyId { get; private set; }
        public bool Connected { get { return conn != null && conn.Alive; } }
        public Action<string> OnLog;

        public Action<MsgType, NetReader> OnMessage;

        public Action OnWelcome;

        public bool Connect(string ip, int port)
        {
            try
            {
                TcpClient tcp = new TcpClient();
                tcp.Connect(ip, port);
                conn = new FramedConnection(tcp);
                Log("접속 성공 → " + ip + ":" + port);
                return true;
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.ConnectionRefused)
                    Log("접속 거부 — 호스트가 켜져 있는지, IP/포트가 맞는지 확인하세요.");
                else if (e.SocketErrorCode == SocketError.TimedOut)
                    Log("시간 초과 — IP·방화벽·같은 와이파이인지 확인하세요.");
                else
                    Log("접속 실패: " + e.Message);
                return false;
            }
            catch (Exception e)
            {
                Log("접속 실패: " + e.Message);
                return false;
            }
        }

        public void Disconnect()
        {
            if (conn != null)
                conn.Kill();
            conn = null;
            MyId = 0;
            Log("연결 종료");
        }

        public void Poll()
        {
            if (conn == null)
                return;

            bool wasAlive = conn.Alive;
            conn.Poll(HandleMessage);

            if (wasAlive && !conn.Alive)
                Log("호스트와 연결이 끊어졌습니다 (" + conn.LastError + ")");
        }

        private void HandleMessage(NetReader r)
        {
            MsgType type = r.ReadMsgType();

            switch (type)
            {
                case MsgType.Welcome:
                    MyId = r.ReadInt();
                    Log("환영합니다 — 당신은 P" + MyId + " 입니다.");
                    OnWelcome?.Invoke();
                    break;

                case MsgType.PlayerJoined:
                    Log("P" + r.ReadInt() + " 님이 입장했습니다.");
                    break;

                case MsgType.PlayerLeft:
                    Log("P" + r.ReadInt() + " 님이 나갔습니다.");
                    break;

                default:
                    OnMessage?.Invoke(type, r);
                    break;
            }
        }

        public void Send(NetWriter w)
        {
            if (conn != null)
                conn.Send(w);
        }

        private void Log(string msg)
        {
            OnLog?.Invoke("[클라] " + msg);
        }
    }
}
