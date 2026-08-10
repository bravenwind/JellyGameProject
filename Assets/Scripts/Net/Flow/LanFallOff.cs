using UnityEngine;

namespace JellyNet
{
    public class LanFallOff : MonoBehaviour
    {
        [Header("판정")]
        [Tooltip("바닥보다 이만큼 아래로 내려가면 낙사. 맵 위치와 무관하게 동작한다.")]
        public float fallDepth = 15f;

        [Tooltip("켜면 DataManager.fallOffThreshold를 절대 좌표로 쓴다. (맵이 원점 근처일 때만)")]
        public bool useAbsoluteThreshold = false;

        [Tooltip("게임 시작 직후 이 시간 동안은 판정하지 않는다. 스폰 직후 바닥에 안착하는 순간을 피한다.")]
        public float graceAfterStart = 1.5f;

        [Tooltip("몇 초마다 확인할지. 매 프레임 볼 이유가 없다.")]
        public float checkInterval = 0.2f;

        [Header("진단")]
        public bool verboseLog = true;

        private float timer;
        private float grace;
        private bool hasReference;
        private float referenceY;
        private GamePhase lastPhase = GamePhase.None;

        private void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None)
                return;

            LanGameFlow flow = LanGameFlow.Instance;
            if (flow == null)
                return;

            if (flow.Phase != lastPhase)
            {
                if (flow.Phase == GamePhase.Playing)
                {
                    hasReference = false;
                    grace = graceAfterStart;
                }
                lastPhase = flow.Phase;
            }

            if (flow.Phase != GamePhase.Playing)
                return;

            if (grace > 0f)
            {
                grace -= Time.deltaTime;
                return;
            }

            if (!hasReference && !CaptureReference())
                return;

            timer += Time.deltaTime;
            if (timer < checkInterval)
                return;
            timer = 0f;

            float y = Threshold;
            CheckMyPlayer(y);
            if (net.IsHost)
                CheckBots(y);
        }

        private bool CaptureReference()
        {
            foreach (LanPlayerState p in EntityRegistry.Players)
            {
                if (p == null || !p.IsMine)
                    continue;

                referenceY = p.transform.position.y;
                hasReference = true;

                if (verboseLog)
                    Debug.Log("[낙사] 바닥 높이 " + referenceY.ToString("F1")
                              + " → 판정선 " + Threshold.ToString("F1"));
                return true;
            }
            return false;
        }

        public float Threshold
        {
            get
            {
                if (useAbsoluteThreshold)
                    return DataManager.Instance != null ? DataManager.Instance.fallOffThreshold : -10f;

                return referenceY - fallDepth;
            }
        }

        private void CheckMyPlayer(float threshold)
        {
            foreach (LanPlayerState p in EntityRegistry.Players)
            {
                if (p == null || !p.IsMine || p.IsOutOfPlay)
                    continue;
                if (p.transform.position.y > threshold)
                    continue;

                if (verboseLog)
                    Debug.Log("[낙사] 내 캐릭터 y=" + p.transform.position.y.ToString("F1")
                              + " ≤ " + threshold.ToString("F1"));

                if (LanGameFlow.Instance != null)
                    LanGameFlow.Instance.ReportSelfEliminated(p.EntityId, "떨어졌습니다!");
                break;
            }
        }

        private void CheckBots(float threshold)
        {
            var bots = EntityRegistry.Bots;
            for (int i = 0; i < bots.Count; i++)
            {
                AIPlayerMovement b = bots[i];
                if (b == null || b.IsOutOfPlay)
                    continue;
                if (b.transform.position.y > threshold)
                    continue;

                b.OnEliminated();
            }
        }
    }
}
