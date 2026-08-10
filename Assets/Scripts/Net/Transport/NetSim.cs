using System.Diagnostics;

namespace JellyNet
{
    public static class NetSim
    {
        public static bool Enabled = false;

        public static float LatencyMs = 30f;

        public static float JitterMs = 5f;

        public static float LossPercent = 0f;

        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly System.Random rng = new System.Random();

        public static double NowMs { get { return clock.Elapsed.TotalMilliseconds; } }

        public static bool ShouldDrop()
        {
            if (LossPercent <= 0f)
                return false;
            return rng.NextDouble() * 100.0 < LossPercent;
        }

        public static double NextDelayMs()
        {
            double jitter = (rng.NextDouble() * 2.0 - 1.0) * JitterMs;
            double d = LatencyMs + jitter;
            return d < 0 ? 0 : d;
        }

        public static void PresetLocal() { Enabled = false; LatencyMs = 0; JitterMs = 0; LossPercent = 0; }
        public static void PresetWifi() { Enabled = true; LatencyMs = 10; JitterMs = 4; LossPercent = 0; }
        public static void PresetRemote() { Enabled = true; LatencyMs = 30; JitterMs = 8; LossPercent = 0; }
        public static void PresetBad() { Enabled = true; LatencyMs = 120; JitterMs = 40; LossPercent = 2; }

        public static string Describe()
        {
            if (!Enabled)
                return "꺼짐 (실제 지연 그대로)";
            return "편도 " + LatencyMs.ToString("F0") + "ms ±" + JitterMs.ToString("F0")
                 + "ms, 손실 " + LossPercent.ToString("F1") + "%";
        }
    }
}
