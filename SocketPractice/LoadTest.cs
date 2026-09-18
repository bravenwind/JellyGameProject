using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SocketPractice
{
    /// <summary>
    /// [4단계] 호스트 부하 테스트.
    ///
    /// 가상 클라이언트를 한 프로세스 안에서 N개 만들어 3단계 호스트에 붙이고,
    /// 각자 메시지를 쏟아부어 호스트가 얼마나 견디는지 측정한다.
    ///
    /// 측정 항목:
    ///   · 처리량(초당 메시지 수)
    ///   · 왕복 지연 RTT — 내가 보낸 메시지가 호스트를 거쳐 나에게 되돌아오는 시간
    ///   · 브로드캐스트 증폭 — 호스트가 실제로 내보낸 총 메시지 수
    ///
    /// 원리: 3단계 호스트는 채팅을 전원에게 브로드캐스트한다(보낸 사람 포함).
    ///       그래서 "L3#17" 같은 꼬리표를 붙여 보내면 그게 나에게 돌아오고,
    ///       보낸 시각과 받은 시각의 차이가 곧 왕복 지연이 된다.
    /// </summary>
    static class LoadTest
    {
        const int Port = 7779;   // 3단계 호스트와 같은 포트

        class VirtualClient
        {
            public int Index;
            public string Tag;                 // "L0#", "L1#" ...
            public TcpClient Conn;
            public NetworkStream Stream;

            public readonly Dictionary<int, long> SendAt = new Dictionary<int, long>();
            public readonly List<double> Rtts = new List<double>();
            public readonly object Lock = new object();

            public int TotalReceived;          // 남의 메시지까지 포함해 받은 전체
            public int EchoMatched;            // 그중 '내가 보낸 것'이 돌아온 수
            public int Sent;                   // 실제로 보낸 개수
            public long BytesReceived;         // 받은 총 바이트(대역폭 계산용)
            public volatile bool Running = true;
        }

        public static void Run(string ip, int clientCount, int messagesPerClient, int intervalMs)
        {
            Console.WriteLine("=== 호스트 부하 테스트 ===");
            Console.WriteLine("대상        : " + ip + ":" + Port);
            Console.WriteLine("가상 클라   : " + clientCount + "명");
            Console.WriteLine("메시지/클라 : " + messagesPerClient + "개");
            Console.WriteLine("전송 간격   : " + (intervalMs <= 0 ? "없음(최대 속도)" : intervalMs + "ms"));
            Console.WriteLine();

            List<VirtualClient> clients = new List<VirtualClient>();

            // ── 1) 전원 접속 ──────────────────────────────
            for (int i = 0; i < clientCount; i++)
            {
                VirtualClient vc = new VirtualClient();
                vc.Index = i;
                vc.Tag = "L" + i + "#";

                try
                {
                    vc.Conn = new TcpClient();
                    vc.Conn.Connect(ip, Port);
                    vc.Conn.NoDelay = true;          // 지연을 재는 것이므로 Nagle을 끈다
                    vc.Stream = vc.Conn.GetStream();
                }
                catch (Exception e)
                {
                    Console.WriteLine("[부하] " + i + "번 클라 접속 실패: " + e.Message);
                    Console.WriteLine("       호스트를 먼저 켰나요?  dotnet run -- step3-host quiet");
                    CloseAll(clients);
                    return;
                }

                clients.Add(vc);
            }

            Console.WriteLine("[부하] " + clients.Count + "명 접속 완료.");

            // ── 2) 수신 스레드 시작 ───────────────────────
            List<Thread> receivers = new List<Thread>();
            foreach (VirtualClient vc in clients)
            {
                VirtualClient captured = vc;
                Thread t = new Thread(() => ReceiveLoop(captured));
                t.IsBackground = true;
                t.Start();
                receivers.Add(t);
            }

            Thread.Sleep(300);   // 입장(JOIN) 브로드캐스트가 정리될 시간

            // ── 3) 일제히 전송 ────────────────────────────
            Console.WriteLine("[부하] 전송 시작...");
            Console.WriteLine();

            Stopwatch sw = Stopwatch.StartNew();

            List<Thread> senders = new List<Thread>();
            foreach (VirtualClient vc in clients)
            {
                VirtualClient captured = vc;
                Thread t = new Thread(() => SendLoop(captured, messagesPerClient, intervalMs));
                t.IsBackground = true;
                t.Start();
                senders.Add(t);
            }

            foreach (Thread t in senders) t.Join();      // 전송이 다 끝날 때까지 대기
            double sendSeconds = sw.Elapsed.TotalSeconds;

            // 아직 날아오는 중인 응답을 받을 시간을 준다
            Console.WriteLine("[부하] 전송 완료. 응답 수집 중(1.5초)...");
            Thread.Sleep(1500);

            sw.Stop();
            foreach (VirtualClient vc in clients) vc.Running = false;

            PrintReport(clients, messagesPerClient, sendSeconds, sw.Elapsed.TotalSeconds);
            CloseAll(clients);
        }

        // ═════════════════════════════════════════════
        //  [5단계] 게임과 같은 조건으로 측정
        //
        //  4단계와 다른 점:
        //   · Thread.Sleep 대신 Stopwatch 기반 정확한 주기 제어 (20Hz면 진짜 20Hz)
        //   · 실제 위치 패킷 크기를 흉내낸 페이로드
        //   · "몇 개 보낼까"가 아니라 "몇 초 동안 몇 Hz로" 라는 게임식 설정
        //   · 클라마다 시작 시점을 흩어 놓음(실제로는 각자 프레임 타이밍이 다르므로)
        // ═════════════════════════════════════════════
        public static void RunGame(string ip, int players, int hz, int seconds, int payloadBytes)
        {
            double intervalMs = 1000.0 / hz;
            int perClient = hz * seconds;

            Console.WriteLine("=== 게임 조건 부하 테스트 ===");
            Console.WriteLine("대상        : " + ip + ":" + Port);
            Console.WriteLine("플레이어    : " + players + "명");
            Console.WriteLine("전송 주기   : " + hz + "Hz  (" + intervalMs.ToString("F1") + "ms 간격)");
            Console.WriteLine("지속 시간   : " + seconds + "초");
            Console.WriteLine("패킷 크기   : 약 " + payloadBytes + "바이트 (위치 패킷 흉내)");
            Console.WriteLine("예상 전송량 : " + (players * perClient) + "개");
            Console.WriteLine();

            List<VirtualClient> clients = new List<VirtualClient>();

            for (int i = 0; i < players; i++)
            {
                VirtualClient vc = new VirtualClient();
                vc.Index = i;
                vc.Tag = "L" + i + "#";
                try
                {
                    vc.Conn = new TcpClient();
                    vc.Conn.Connect(ip, Port);
                    vc.Conn.NoDelay = true;
                    vc.Stream = vc.Conn.GetStream();
                }
                catch (Exception e)
                {
                    Console.WriteLine("[게임부하] " + i + "번 접속 실패: " + e.Message);
                    Console.WriteLine("           호스트를 먼저 켜세요:  dotnet run -- step3-host quiet");
                    CloseAll(clients);
                    return;
                }
                clients.Add(vc);
            }

            Console.WriteLine("[게임부하] " + players + "명 접속 완료.");

            foreach (VirtualClient vc in clients)
            {
                VirtualClient captured = vc;
                Thread t = new Thread(() => ReceiveLoop(captured));
                t.IsBackground = true;
                t.Start();
            }

            Thread.Sleep(300);
            Console.WriteLine("[게임부하] " + seconds + "초 동안 전송합니다...");
            Console.WriteLine();

            Stopwatch sw = Stopwatch.StartNew();

            List<Thread> senders = new List<Thread>();
            Random rnd = new Random(12345);
            foreach (VirtualClient vc in clients)
            {
                VirtualClient captured = vc;
                // 실제 게임에서는 각 클라의 프레임 타이밍이 제각각이므로 시작을 흩뿌린다.
                // (전원이 정확히 같은 순간에 쏘면 인위적인 몰림이 생겨 결과가 왜곡된다)
                double offset = rnd.NextDouble() * intervalMs;
                Thread t = new Thread(() => SendLoopPaced(captured, perClient, intervalMs, payloadBytes, offset));
                t.IsBackground = true;
                t.Start();
                senders.Add(t);
            }

            foreach (Thread t in senders) t.Join();
            double sendSeconds = sw.Elapsed.TotalSeconds;

            Console.WriteLine("[게임부하] 전송 완료. 응답 수집 중(1초)...");
            Thread.Sleep(1000);
            sw.Stop();
            foreach (VirtualClient vc in clients) vc.Running = false;

            PrintGameReport(clients, hz, seconds, payloadBytes, sendSeconds);
            CloseAll(clients);
        }

        /// <summary>Stopwatch 기준으로 '목표 시각'에 맞춰 보내는 정확한 주기 전송.</summary>
        static void SendLoopPaced(VirtualClient vc, int count, double intervalMs,
                                  int payloadBytes, double startOffsetMs)
        {
            double tickPerMs = Stopwatch.Frequency / 1000.0;
            long t0 = Stopwatch.GetTimestamp();

            for (int seq = 0; seq < count; seq++)
            {
                // seq번째 메시지를 보내야 할 '목표 시각'
                double dueMs = startOffsetMs + seq * intervalMs;
                WaitUntil(t0, dueMs, tickPerMs);

                lock (vc.Lock) { vc.SendAt[seq] = Stopwatch.GetTimestamp(); }

                try { MessageIO.Send(vc.Stream, BuildPayload(vc, seq, payloadBytes)); }
                catch { break; }

                vc.Sent++;
            }
        }

        /// <summary>
        /// 목표 시각까지 대기.
        /// Thread.Sleep은 윈도우에서 15.6ms 단위로 튀므로, 남은 시간이 짧아지면
        /// SpinWait(CPU를 쓰며 짧게 대기)으로 바꿔 정밀도를 맞춘다.
        /// </summary>
        static void WaitUntil(long t0, double dueMs, double tickPerMs)
        {
            while (true)
            {
                double elapsed = (Stopwatch.GetTimestamp() - t0) / tickPerMs;
                double remain = dueMs - elapsed;

                if (remain <= 0.05) return;      // 도착
                if (remain > 16) Thread.Sleep(1);  // 아직 여유 -> 양보하며 대기
                else Thread.SpinWait(200);         // 얼마 안 남음 -> 정밀 대기
            }
        }

        /// <summary>실제 위치 패킷을 흉내낸 페이로드. 앞부분은 RTT 매칭용 태그+순번.</summary>
        static string BuildPayload(VirtualClient vc, int seq, int targetBytes)
        {
            // 예: "L0#37|12.5,3.0,-8.2|" + 남은 만큼 채우기
            string head = vc.Tag + seq + "|"
                        + (vc.Index * 3.5).ToString("F1") + ","
                        + (seq % 100) + ".0,0.0|";

            int pad = targetBytes - head.Length;
            if (pad > 0) head += new string('.', pad);
            return head;
        }

        static void PrintGameReport(List<VirtualClient> clients, int hz, int seconds,
                                    int payloadBytes, double sendSeconds)
        {
            int n = clients.Count;
            int targetTotal = n * hz * seconds;

            int totalSent = 0, totalReceived = 0, totalMatched = 0;
            long totalBytes = 0;
            List<double> rtts = new List<double>();

            foreach (VirtualClient vc in clients)
            {
                totalSent += vc.Sent;
                totalReceived += vc.TotalReceived;
                totalBytes += vc.BytesReceived;
                lock (vc.Lock)
                {
                    totalMatched += vc.EchoMatched;
                    rtts.AddRange(vc.Rtts);
                }
            }
            rtts.Sort();

            double achievedHz = totalSent / Math.Max(sendSeconds, 0.001) / n;
            double kbPerSec = (totalBytes / 1024.0) / Math.Max(sendSeconds, 0.001);

            Console.WriteLine("──────────────────────────────────────────");
            Console.WriteLine("  게임 조건 결과");
            Console.WriteLine("──────────────────────────────────────────");
            Console.WriteLine("  실제 소요       : " + sendSeconds.ToString("F2") + "초 (목표 " + seconds + "초)");
            Console.WriteLine("  전송 개수       : " + totalSent + " / 목표 " + targetTotal
                              + "  (달성률 " + (100.0 * totalSent / Math.Max(targetTotal, 1)).ToString("F1") + "%)");
            Console.WriteLine("  실제 전송 주기  : " + achievedHz.ToString("F1") + " Hz / 클라   (목표 " + hz + " Hz)");
            Console.WriteLine();
            Console.WriteLine("  호스트 송신량   : " + totalReceived + "개  ("
                              + (totalReceived / Math.Max(sendSeconds, 0.001)).ToString("F0") + " msg/s)");
            Console.WriteLine("  호스트 업로드   : " + kbPerSec.ToString("F1") + " KB/s   <- 와이파이 부담 지표");
            Console.WriteLine("  브로드캐스트 증폭: x" + ((double)totalReceived / Math.Max(totalSent, 1)).ToString("F1"));
            Console.WriteLine();

            if (rtts.Count > 0)
            {
                double p95 = Percentile(rtts, 95);
                double p99 = Percentile(rtts, 99);
                Console.WriteLine("  왕복 지연(RTT)");
                Console.WriteLine("    최소/평균/중앙값 : " + rtts[0].ToString("F2") + " / "
                                  + rtts.Average().ToString("F2") + " / " + Percentile(rtts, 50).ToString("F2") + " ms");
                Console.WriteLine("    상위95% / 99%    : " + p95.ToString("F2") + " / " + p99.ToString("F2") + " ms");
                Console.WriteLine("    최대             : " + rtts[rtts.Count - 1].ToString("F2") + " ms");
                Console.WriteLine();
                Console.WriteLine("    60fps 기준 환산  : 상위95%가 " + (p95 / 16.67).ToString("F2") + " 프레임 지연");
            }

            int lost = totalSent - totalMatched;
            Console.WriteLine();
            Console.WriteLine("  응답 못 받음    : " + lost + "개");
            Console.WriteLine("──────────────────────────────────────────");
            Console.WriteLine();
            Console.WriteLine("  * 해석 기준");
            Console.WriteLine("    - 달성률이 95% 미만이면 클라가 목표 주기를 못 지킨 것(CPU/전송 병목)");
            Console.WriteLine("    - 상위95% RTT가 50ms를 넘으면 캐릭터 움직임이 눈에 띄게 밀린다");
            Console.WriteLine("    - 로컬(127.0.0.1)은 실제 와이파이보다 훨씬 좋게 나오니, 실기기 테스트가 별도로 필요");
        }

        // ─────────────────────────────────────────────
        static void SendLoop(VirtualClient vc, int count, int intervalMs)
        {
            for (int seq = 0; seq < count; seq++)
            {
                // 보낸 시각을 기록해 두었다가, 돌아왔을 때 차이를 잰다
                lock (vc.Lock) { vc.SendAt[seq] = Stopwatch.GetTimestamp(); }

                try
                {
                    MessageIO.Send(vc.Stream, vc.Tag + seq);
                }
                catch
                {
                    break;   // 연결이 끊기면 이 클라는 중단
                }

                if (intervalMs > 0) Thread.Sleep(intervalMs);
            }
        }

        static void ReceiveLoop(VirtualClient vc)
        {
            while (vc.Running)
            {
                string msg;
                try { msg = MessageIO.Receive(vc.Stream); }
                catch { break; }

                Interlocked.Increment(ref vc.TotalReceived);
                // 실제로 선을 타고 온 바이트 = 본문 + 길이 헤더 4바이트
                Interlocked.Add(ref vc.BytesReceived, Encoding.UTF8.GetByteCount(msg) + 4);

                // 호스트는 "CHAT P3: L2#17" 형태로 되돌려 준다 -> 내 꼬리표가 있는지 확인
                int at = msg.IndexOf(vc.Tag, StringComparison.Ordinal);
                if (at < 0) continue;

                int p = at + vc.Tag.Length;
                int end = p;
                while (end < msg.Length && char.IsDigit(msg[end])) end++;
                if (end == p) continue;

                int seq;
                if (!int.TryParse(msg.Substring(p, end - p), out seq)) continue;

                long now = Stopwatch.GetTimestamp();
                lock (vc.Lock)
                {
                    long sentAt;
                    if (vc.SendAt.TryGetValue(seq, out sentAt))
                    {
                        vc.SendAt.Remove(seq);
                        vc.Rtts.Add((now - sentAt) * 1000.0 / Stopwatch.Frequency);   // ms
                        vc.EchoMatched++;
                    }
                }
            }
        }

        // ─────────────────────────────────────────────
        static void PrintReport(List<VirtualClient> clients, int messagesPerClient,
                                double sendSeconds, double totalSeconds)
        {
            int n = clients.Count;
            int totalSent = n * messagesPerClient;

            int totalReceived = 0;
            int totalMatched = 0;
            List<double> rtts = new List<double>();

            foreach (VirtualClient vc in clients)
            {
                totalReceived += vc.TotalReceived;
                lock (vc.Lock)
                {
                    totalMatched += vc.EchoMatched;
                    rtts.AddRange(vc.Rtts);
                }
            }

            rtts.Sort();

            Console.WriteLine("──────────────────────────────────────────");
            Console.WriteLine("  결과");
            Console.WriteLine("──────────────────────────────────────────");
            Console.WriteLine("  전송 소요 시간   : " + sendSeconds.ToString("F2") + "초");
            Console.WriteLine("  보낸 메시지      : " + totalSent + "개");
            Console.WriteLine("  전송 처리량      : " + (totalSent / Math.Max(sendSeconds, 0.001)).ToString("F0") + " msg/s");
            Console.WriteLine();
            Console.WriteLine("  호스트가 내보낸 총량(= 전체 수신 합): " + totalReceived + "개");
            Console.WriteLine("  브로드캐스트 증폭 : x" + ((double)totalReceived / Math.Max(totalSent, 1)).ToString("F1")
                              + "  (클라 " + n + "명에게 각각 전달하므로)");
            Console.WriteLine("  호스트 송신 처리량: " + (totalReceived / Math.Max(sendSeconds, 0.001)).ToString("F0") + " msg/s");
            Console.WriteLine();

            if (rtts.Count > 0)
            {
                Console.WriteLine("  왕복 지연(RTT) — 내 메시지가 호스트를 거쳐 돌아오기까지");
                Console.WriteLine("    최소   : " + rtts[0].ToString("F2") + " ms");
                Console.WriteLine("    평균   : " + rtts.Average().ToString("F2") + " ms");
                Console.WriteLine("    중앙값 : " + Percentile(rtts, 50).ToString("F2") + " ms");
                Console.WriteLine("    상위95%: " + Percentile(rtts, 95).ToString("F2") + " ms   <- 체감 끊김은 여기서 드러남");
                Console.WriteLine("    최대   : " + rtts[rtts.Count - 1].ToString("F2") + " ms");
            }
            else
            {
                Console.WriteLine("  [경고] 되돌아온 메시지가 없습니다. 호스트가 3단계(step3-host)인지 확인하세요.");
            }

            int lost = totalSent - totalMatched;
            Console.WriteLine();
            Console.WriteLine("  응답 못 받은 메시지: " + lost + "개"
                              + (lost > 0 ? "   (수집 시간 부족이거나 호스트가 밀린 것)" : ""));
            Console.WriteLine("──────────────────────────────────────────");
        }

        static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int idx = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
            idx = Math.Clamp(idx, 0, sorted.Count - 1);
            return sorted[idx];
        }

        static void CloseAll(List<VirtualClient> clients)
        {
            foreach (VirtualClient vc in clients)
            {
                vc.Running = false;
                try { if (vc.Stream != null) vc.Stream.Close(); } catch { }
                try { if (vc.Conn != null) vc.Conn.Close(); } catch { }
            }
        }
    }
}
