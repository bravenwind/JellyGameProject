using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace JellyNet
{
    /// <summary>
    /// 참가자(클라이언트) 측 로직.
    /// 호스트와의 연결 하나만 관리한다. 역시 Poll()을 매 프레임 호출.
    /// </summary>
    public class NetClient
    {
        FramedConnection _conn;
        readonly NetWriter _w = new NetWriter();

        // 핑 왕복 측정용
        readonly Stopwatch _clock = Stopwatch.StartNew();
        long _pingSentAt;
        int _pingSeq;

        public int MyId { get; private set; }
        public bool Connected { get { return _conn != null && _conn.Alive; } }
        public double LastRttMs { get; private set; }

        public Action<string> OnLog;
        public Action<NetReader> OnMessage;

        // ─────────────────────────────────────────────
        public bool Connect(string ip, int port)
        {
            try
            {
                TcpClient tcp = new TcpClient();
                tcp.Connect(ip, port);
                _conn = new FramedConnection(tcp);
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
            if (_conn != null) _conn.Close();
            _conn = null;
            MyId = 0;
            Log("연결 종료");
        }

        // ─────────────────────────────────────────────
        public void Poll()
        {
            if (_conn == null) return;

            bool wasAlive = _conn.Alive;
            _conn.Poll(HandleMessage);

            if (wasAlive && !_conn.Alive)
                Log("호스트와 연결이 끊어졌습니다 (" + _conn.LastError + ")");
        }

        void HandleMessage(NetReader r)
        {
            MsgType type = r.ReadMsgType();

            switch (type)
            {
                case MsgType.Welcome:
                    MyId = r.ReadInt();
                    Log("환영합니다 — 당신은 P" + MyId + " 입니다.");
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
                        if (seq == _pingSeq)
                        {
                            long now = _clock.ElapsedTicks;
                            LastRttMs = (now - _pingSentAt) * 1000.0 / Stopwatch.Frequency;
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
                    if (OnMessage != null) OnMessage(r);
                    break;
            }
        }

        // ─────────────────────────────────────────────
        public void SendPing()
        {
            if (!Connected) return;
            _pingSeq++;
            _pingSentAt = _clock.ElapsedTicks;

            _w.Begin(MsgType.Ping);
            _w.WriteInt(_pingSeq);
            _w.End();
            Send(_w);
        }

        public void SendChat(string text)
        {
            if (!Connected || string.IsNullOrEmpty(text)) return;

            _w.Begin(MsgType.Chat);
            _w.WriteString(text);
            _w.End();
            Send(_w);
        }

        public void Send(NetWriter w)
        {
            if (_conn != null) _conn.Send(w);
        }

        void Log(string msg)
        {
            if (OnLog != null) OnLog("[클라] " + msg);
        }
    }
}
