using System.Diagnostics;

namespace JellyNet
{
    /// <summary>
    /// 네트워크 상태 시뮬레이터.
    ///
    /// 같은 PC에서 두 인스턴스를 띄우면 지연이 0.1ms라, 실제 원격(20~60ms)에서만
    /// 드러나는 문제(특히 '보간이 없어서 뚝뚝 끊기는' 현상)를 절대 볼 수 없다.
    /// 그래서 받은 메시지를 일부러 늦게 처리해 원격 환경을 흉내낸다.
    ///
    /// 사용법: NetSim.Enabled = true; NetSim.LatencyMs = 30; ...
    ///        (테스트 UI에서 실행 중에도 바꿀 수 있다)
    ///
    /// ★ 값은 모두 '편도' 기준이다. 호스트·클라 양쪽에 30ms를 걸면 RTT는 약 60ms가 된다.
    /// </summary>
    public static class NetSim
    {
        /// <summary>꺼져 있으면 아무 비용도 들지 않는다(그냥 즉시 처리).</summary>
        public static bool Enabled = false;

        /// <summary>편도 지연(ms). 같은 와이파이 5~15, 국내 원격 20~40, 해외 100~200.</summary>
        public static float LatencyMs = 30f;

        /// <summary>지연의 흔들림(±ms). 무선 환경은 지터가 크다.</summary>
        public static float JitterMs = 5f;

        /// <summary>패킷 손실률(%). 아래 주의사항 참고.</summary>
        public static float LossPercent = 0f;

        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static readonly System.Random _rng = new System.Random();

        public static double NowMs { get { return _clock.Elapsed.TotalMilliseconds; } }

        /// <summary>이번 메시지를 버릴지 결정.</summary>
        public static bool ShouldDrop()
        {
            if (LossPercent <= 0f) return false;
            return _rng.NextDouble() * 100.0 < LossPercent;
        }

        /// <summary>이번 메시지에 적용할 지연(ms). 지터를 더해 흔들리게 만든다.</summary>
        public static double NextDelayMs()
        {
            double jitter = (_rng.NextDouble() * 2.0 - 1.0) * JitterMs;   // -Jitter ~ +Jitter
            double d = LatencyMs + jitter;
            return d < 0 ? 0 : d;
        }

        // ── 자주 쓰는 프리셋 ──
        public static void PresetLocal() { Enabled = false; LatencyMs = 0; JitterMs = 0; LossPercent = 0; }
        public static void PresetWifi() { Enabled = true; LatencyMs = 10; JitterMs = 4; LossPercent = 0; }
        public static void PresetRemote() { Enabled = true; LatencyMs = 30; JitterMs = 8; LossPercent = 0; }
        public static void PresetBad() { Enabled = true; LatencyMs = 120; JitterMs = 40; LossPercent = 2; }

        public static string Describe()
        {
            if (!Enabled) return "꺼짐 (실제 지연 그대로)";
            return "편도 " + LatencyMs.ToString("F0") + "ms ±" + JitterMs.ToString("F0")
                 + "ms, 손실 " + LossPercent.ToString("F1") + "%";
        }
    }
}

// ─────────────────────────────────────────────────────────────
//  [주의] TCP에서 '손실'의 의미
//
//  실제 TCP는 패킷이 유실되어도 재전송으로 복구하므로, 애플리케이션에서
//  메시지가 사라지는 일은 없다. 유실은 '사라짐'이 아니라 '지연 급증'으로 나타난다
//  (앞 패킷이 도착할 때까지 뒤 패킷도 대기 — head-of-line blocking).
//
//  따라서 LossPercent는
//   · TCP 흉내로는 부정확하다 → 지연·지터로 실험하는 것이 더 현실적
//   · 나중에 위치 동기화를 UDP로 옮길 때를 대비한 실험용으로 남겨둔다
// ─────────────────────────────────────────────────────────────
