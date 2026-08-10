using System;
using System.Collections.Generic;
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

        struct Delayed
        {
            public byte[] Data;
            public double ReleaseAtMs;
        }
        private readonly Queue<Delayed> delayQueue = new Queue<Delayed>();
        private double lastReleaseAtMs;

        public bool Alive { get; private set; }
        public string LastError { get; private set; }

        public static bool Trace = false;

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
                if (Trace && w.Length >= 5)
                    UnityEngine.Debug.Log("[Trace] 송신 type=" + w.Buffer[4] + " 총" + w.Length + "바이트");

                stream.Write(w.Buffer, 0, w.Length);
            }
            catch (Exception e)
            {
                Kill("전송 실패: " + e.Message);
            }
        }

        public void Poll(Action<NetReader> onMessage)
        {
            DrainDelayed(onMessage);

            if (!Alive)
                return;

            try
            {
                int avail = client.Available;
                if (avail > 0)
                {
                    Ensure(len + avail);
                    int n = stream.Read(buf, len, avail);
                    if (n <= 0)
                    {
                        Kill("상대가 연결을 닫았습니다.");
                        return;
                    }
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

                    if (Trace)
                        UnityEngine.Debug.Log("[Trace] 처리 type=" + buf[offset + 4]
                            + " bodyLen=" + bodyLen + " offset=" + offset + " len=" + len);

                    if (!NetSim.Enabled)
                    {
                        reader.Reset(buf, offset + 4, bodyLen);
                        onMessage(reader);
                    }
                    else
                    {
                        EnqueueDelayed(buf, offset + 4, bodyLen);
                    }

                    offset += 4 + bodyLen;
                }

                if (offset > 0)
                {
                    int rest = len - offset;
                    if (rest > 0)
                        Buffer.BlockCopy(buf, offset, buf, 0, rest);
                    len = rest;

                    if (Trace)
                        UnityEngine.Debug.Log("[Trace] 정리 offset=" + offset + " → 남은 len=" + len);
                }
                else if (Trace && len > 0)
                {
                    UnityEngine.Debug.Log("[Trace] ★진행 없음! len=" + len + " (같은 데이터를 다음 프레임에 또 봄)");
                }
            }
            catch (Exception e)
            {
                Kill("수신 오류: " + e.Message);
            }
        }

        private void EnqueueDelayed(byte[] src, int offset, int length)
        {
            if (NetSim.ShouldDrop())
                return;

            double release = NetSim.NowMs + NetSim.NextDelayMs();

            if (release < lastReleaseAtMs)
                release = lastReleaseAtMs;
            lastReleaseAtMs = release;

            Delayed d;
            d.Data = new byte[length];
            Buffer.BlockCopy(src, offset, d.Data, 0, length);
            d.ReleaseAtMs = release;
            delayQueue.Enqueue(d);
        }

        private void DrainDelayed(Action<NetReader> onMessage)
        {
            if (delayQueue.Count == 0)
                return;

            double now = NetSim.NowMs;
            while (delayQueue.Count > 0 && delayQueue.Peek().ReleaseAtMs <= now)
            {
                Delayed d = delayQueue.Dequeue();
                reader.Reset(d.Data, 0, d.Data.Length);
                onMessage(reader);
            }
        }

        public void Close()
        {
            Kill("정상 종료");
        }

        private void Kill(string reason)
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
