using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace JellyNet
{
    public class NetClient
    {
        private FramedConnection conn;
        private readonly NetWriter writer = new NetWriter();

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long pingSentAt;
        private int pingSeq;

        public int MyId { get; private set; }
        public bool Connected { get { return conn != null && conn.Alive; } }
        public double LastRttMs { get; private set; }

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
                conn.Close();
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
                    if (OnWelcome != null)
                        OnWelcome();
                    break;

                case MsgType.PlayerJoined:
                    Log("P" + r.ReadInt() + " 님이 입장했습니다.");
                    break;

                case MsgType.PlayerLeft:
                    Log("P" + r.ReadInt() + " 님이 나갔습니다.");
                    break;

                case MsgType.Pong:
                    {
                        int seq = r.ReadInt();
                        if (seq == pingSeq)
                        {
                            long now = clock.ElapsedTicks;
                            LastRttMs = (now - pingSentAt) * 1000.0 / Stopwatch.Frequency;
                            Log("RTT " + LastRttMs.ToString("F2") + " ms");
                        }
                        break;
                    }

                case MsgType.Chat:
                    {
                        int from = r.ReadInt();
                        string text = r.ReadString();
                        Log("P" + from + ": " + text);
                        break;
                    }

                default:
                    if (OnMessage != null)
                        OnMessage(type, r);
                    break;
            }
        }

        public void SendPing()
        {
            if (!Connected)
                return;
            pingSeq++;
            pingSentAt = clock.ElapsedTicks;

            writer.Begin(MsgType.Ping);
            writer.WriteInt(pingSeq);
            writer.End();
            Send(writer);
        }

        public void SendChat(string text)
        {
            if (!Connected || string.IsNullOrEmpty(text))
                return;

            writer.Begin(MsgType.Chat);
            writer.WriteString(text);
            writer.End();
            Send(writer);
        }

        public void Send(NetWriter w)
        {
            if (conn != null)
                conn.Send(w);
        }

        private void Log(string msg)
        {
            if (OnLog != null)
                OnLog("[클라] " + msg);
        }
    }
}
