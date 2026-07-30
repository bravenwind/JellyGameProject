using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace JellyNet
{
    /// <summary>
    /// 연결 하나(TcpClient)를 감싸서 "메시지 단위" 송수신을 제공한다.
    /// 이 클래스가 2단계의 핵심이다.
    ///
    /// ★ 스레드를 쓰지 않는다(폴링 방식).
    ///   Unity의 Update()에서 Poll()을 매 프레임 부르면, Available로 "읽을 게 있나"만
    ///   확인하고 없으면 즉시 돌아온다. 그래서 게임 루프가 멈추지 않는다.
    ///   덕분에 모든 처리가 메인 스레드에서 일어나 lock·경쟁 상태가 아예 없고,
    ///   transform·Instantiate 같은 Unity API를 바로 호출할 수 있다.
    ///
    /// ★ 대신 '부분 수신'을 직접 모아야 한다.
    ///   블로킹 방식이라면 ReadExactly가 다 올 때까지 기다려줬지만, 폴링은 기다릴 수 없다.
    ///   그래서 누적 버퍼(_buf)에 계속 이어붙이고, 완전한 메시지가 모였을 때만 꺼낸다.
    /// </summary>
    public class FramedConnection
    {
        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly NetReader _reader = new NetReader();   // 재사용(할당 0)

        byte[] _buf = new byte[NetConfig.RecvBufferInitial];
        int _len;                 // 누적 버퍼에 쌓인 유효 바이트 수

        // ── 지연 시뮬레이션용 (NetSim.Enabled일 때만 사용) ──
        struct Delayed
        {
            public byte[] Data;
            public double ReleaseAtMs;
        }
        readonly Queue<Delayed> _delayQueue = new Queue<Delayed>();
        double _lastReleaseAtMs;   // TCP는 순서가 뒤바뀌지 않으므로 방출 시각도 단조 증가시킨다

        public bool Alive { get; private set; }
        public string LastError { get; private set; }

        public FramedConnection(TcpClient tcp)
        {
            _tcp = tcp;
            _tcp.NoDelay = true;         // 실시간이므로 Nagle 끔(작은 패킷도 즉시 전송)
            _stream = tcp.GetStream();
            Alive = true;
        }

        // ─────────────────────────────────────────────
        //  전송
        // ─────────────────────────────────────────────
        public void Send(NetWriter w)
        {
            if (!Alive) return;
            try
            {
                _stream.Write(w.Buffer, 0, w.Length);
            }
            catch (Exception e)
            {
                Kill("전송 실패: " + e.Message);
            }
        }

        // ─────────────────────────────────────────────
        //  수신 (매 프레임 호출)
        // ─────────────────────────────────────────────
        /// <summary>
        /// 도착한 바이트를 모아 완전한 메시지가 생기면 onMessage를 호출한다.
        /// 한 프레임에 여러 개가 처리될 수 있다(그동안 쌓였을 수 있으므로).
        /// </summary>
        public void Poll(Action<NetReader> onMessage)
        {
            // 시뮬레이션 큐에서 '도착할 시각이 된' 메시지부터 먼저 내보낸다
            DrainDelayed(onMessage);

            if (!Alive) return;

            try
            {
                // ── 1) 도착한 만큼 누적 버퍼에 이어붙인다 ──
                //    Available = "지금 읽을 수 있는 바이트 수". 0이면 아무것도 안 하고 넘어간다.
                int avail = _tcp.Available;
                if (avail > 0)
                {
                    Ensure(_len + avail);
                    int n = _stream.Read(_buf, _len, avail);
                    if (n <= 0) { Kill("상대가 연결을 닫았습니다."); return; }
                    _len += n;
                }

                // 연결이 끊겼는지 확인(Available이 0이어도 끊겼을 수 있다)
                if (!_tcp.Connected) { Kill("연결이 끊어졌습니다."); return; }

                // ── 2) 완전한 메시지를 앞에서부터 꺼낸다 ──
                int offset = 0;
                while (_len - offset >= 4)                    // 길이 필드(4바이트)는 있나?
                {
                    int bodyLen = _buf[offset]
                                | (_buf[offset + 1] << 8)
                                | (_buf[offset + 2] << 16)
                                | (_buf[offset + 3] << 24);

                    if (bodyLen <= 0 || bodyLen > NetConfig.MaxBodySize)
                    {
                        Kill("비정상 패킷 길이: " + bodyLen);
                        return;
                    }

                    if (_len - offset - 4 < bodyLen) break;   // 본문이 아직 다 안 왔다 → 다음 프레임에

                    if (!NetSim.Enabled)
                    {
                        // 평소 경로: 복사 없이 버퍼 구간을 그대로 넘긴다(할당 0)
                        _reader.Reset(_buf, offset + 4, bodyLen);
                        onMessage(_reader);                    // 여기서 게임 로직이 처리
                    }
                    else
                    {
                        // 시뮬레이션 경로: 나중에 처리해야 하므로 내용을 복사해 큐에 넣는다
                        // (누적 버퍼는 곧 덮어써지므로 그대로 들고 있을 수 없다)
                        EnqueueDelayed(_buf, offset + 4, bodyLen);
                    }

                    offset += 4 + bodyLen;
                }

                // ── 3) 처리한 만큼 버리고 남은 조각을 앞으로 당긴다 ──
                if (offset > 0)
                {
                    int rest = _len - offset;
                    if (rest > 0) Buffer.BlockCopy(_buf, offset, _buf, 0, rest);
                    _len = rest;
                }
            }
            catch (Exception e)
            {
                Kill("수신 오류: " + e.Message);
            }
        }

        // ─────────────────────────────────────────────
        //  지연 시뮬레이션
        // ─────────────────────────────────────────────
        void EnqueueDelayed(byte[] src, int offset, int length)
        {
            // 손실 시뮬레이션: 그냥 버린다 (TCP 흉내로는 부정확 — NetSim.cs 하단 주석 참고)
            if (NetSim.ShouldDrop()) return;

            double release = NetSim.NowMs + NetSim.NextDelayMs();

            // TCP는 절대 순서가 뒤바뀌지 않는다. 지터 때문에 앞 메시지보다
            // 먼저 나갈 뻔하면, 앞 메시지 시각까지 늦춰 순서를 지킨다.
            if (release < _lastReleaseAtMs) release = _lastReleaseAtMs;
            _lastReleaseAtMs = release;

            Delayed d;
            d.Data = new byte[length];
            Buffer.BlockCopy(src, offset, d.Data, 0, length);
            d.ReleaseAtMs = release;
            _delayQueue.Enqueue(d);
        }

        void DrainDelayed(Action<NetReader> onMessage)
        {
            if (_delayQueue.Count == 0) return;

            double now = NetSim.NowMs;
            while (_delayQueue.Count > 0 && _delayQueue.Peek().ReleaseAtMs <= now)
            {
                Delayed d = _delayQueue.Dequeue();
                _reader.Reset(d.Data, 0, d.Data.Length);
                onMessage(_reader);
            }
        }

        // ─────────────────────────────────────────────
        public void Close()
        {
            Kill("정상 종료");
        }

        void Kill(string reason)
        {
            if (!Alive) return;
            Alive = false;
            LastError = reason;
            try { _stream.Close(); } catch { }
            try { _tcp.Close(); } catch { }
        }

        void Ensure(int need)
        {
            if (need <= _buf.Length) return;
            int cap = _buf.Length * 2;
            if (cap < need) cap = need;
            Array.Resize(ref _buf, cap);
        }
    }
}
