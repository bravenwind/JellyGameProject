using System;
using System.Net.Sockets;

namespace JellyNet
{
    public class FramedConnection
    {
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly NetReader reader = new NetReader();

        private byte[] buf = new byte[NetConfig.RECV_BUFFER_INITIAL];
        private int len;

        public bool Alive { get; private set; }
        public string LastError { get; private set; }

        public FramedConnection(TcpClient tcp)
        {
            client = tcp;

            client.NoDelay = true;

            stream = tcp.GetStream();
            Alive = true;
        }

        public void Send(NetWriter w)
        {
            if (!Alive)
                return;

            try
            {
                stream.Write(w.Buffer, 0, w.Length);
            }
            catch (Exception e)
            {
                Kill("전송 실패: " + e.Message);
            }
        }

        public void Poll(Action<NetReader> onMessage)
        {
            if (!Alive)
                return;

            try
            {
                if (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0)
                {
                    Kill("상대가 연결을 닫았습니다.");
                    return;
                }

                int avail = client.Available;
                if (avail > 0)
                {
                    Ensure(len + avail);

                    int n = stream.Read(buf, len, avail);

                    len += n;
                }

                if (!client.Connected)
                {
                    Kill("연결이 끊어졌습니다.");
                    return;
                }

                int offset = 0;

                while (len - offset >= 4)
                {
                    int bodyLen = buf[offset]
                                | (buf[offset + 1] << 8)
                                | (buf[offset + 2] << 16)
                                | (buf[offset + 3] << 24);

                    if (bodyLen <= 0 || bodyLen > NetConfig.MAX_BODY_SIZE)
                    {
                        Kill("비정상 패킷 길이: " + bodyLen);
                        return;
                    }

                    if (len - offset - 4 < bodyLen)
                        break;

                    reader.Reset(buf, offset + 4, bodyLen);
                    onMessage(reader);

                    offset += 4 + bodyLen;
                }

                if (offset > 0)
                {
                    int rest = len - offset;

                    if (rest > 0)
                        Buffer.BlockCopy(buf, offset, buf, 0, rest);

                    len = rest;
                }
            }
            catch (Exception e)
            {
                Kill("수신 오류: " + e.Message);
            }
        }

        public void Kill(string reason = "정상 종료")
        {
            if (!Alive)
                return;

            Alive = false;
            LastError = reason;

            try { stream.Close(); } catch { }
            try { client.Close(); } catch { }
        }

        private void Ensure(int need)
        {
            if (need <= buf.Length)
                return;

            int cap = buf.Length * 2;

            if (cap < need)
                cap = need;

            Array.Resize(ref buf, cap);
        }
    }
}
