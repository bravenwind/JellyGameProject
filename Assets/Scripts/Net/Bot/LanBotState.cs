using UnityEngine;

namespace JellyNet
{
    public class LanBotState : MonoBehaviour
    {
        [Header("상태 전송")]
        [Tooltip("초당 몇 번 보낼지. 크기·색·점수는 자주 안 변해서 낮아도 된다.")]
        public float scaleSendRate = 5f;

        [Tooltip("이만큼 차이 나야 보낸다. 미세 떨림으로 도배되는 걸 막는다.")]
        public float scaleThreshold = 0.01f;

        private NetIdentity id;
        private PlayerScaleController scaleCtrl;
        private NameTagBillboard nameTag;
        private PlayerColorVisual colorVisual;

        private float sendTimer;
        private float lastSentScale = -1f;
        private float targetScale = -1f;

        private readonly NetWriter w = new NetWriter();

        public string BotName { get; private set; }

        public int CurrentScore { get; private set; }

        // ★ 점수도 방송해야 한다
        //   예전엔 CurrentScore를 호스트에서만 올리고 패킷에는 싣지 않았다.
        //   그래서 클라의 인게임 순위표에서 봇 점수가 언제나 0이었다.
        //   (결과 화면은 호스트가 만든 FinalStandings를 그대로 받아 맞게 보였기 때문에
        //    판이 끝나야 숫자가 갑자기 생기는 것처럼 보였다)
        //   사람 쪽 LanPlayerState는 처음부터 점수를 방송하고 있었다 — 그 짝을 맞춘다.
        public void HostAddScore(int delta)
        {
            if (!IsHost() || delta == 0)
                return;
            CurrentScore += delta;
            HostBroadcastState();
        }

        public void HostSetScore(int score)
        {
            if (!IsHost() || score == CurrentScore)
                return;
            CurrentScore = score;
            HostBroadcastState();
        }

        private static bool IsHost()
        {
            return NetManager.Instance != null && NetManager.Instance.IsHost;
        }

        public bool IsDriver
        {
            //봇은 전부 NetWorld가 스폰하므로 id가 없는 봇은 없다
            get { return id != null && id.IsMineOrOffline; }
        }

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
            scaleCtrl = GetComponent<PlayerScaleController>();
            nameTag = GetComponentInChildren<NameTagBillboard>(true);
            colorVisual = GetComponentInChildren<PlayerColorVisual>(true);
        }

        private void Start()
        {
            BotName = "AI 봇 " + (id != null ? id.NetId : 0);
            gameObject.name = "Bot_" + BotName;

            if (nameTag != null)
            {
                nameTag.SetName(BotName);
                nameTag.ApplyRoleColor(NameTagRole.Bot);
            }
        }

        private void Update()
        {
            if (IsDriver)
                HostSendScale();
            else
                FollowScale();
        }

        //크기·색이 눈에 띄게 변했을 때만 주기적으로 내보낸다.
        //점수는 여기서 계산하지 않는다 — 흡수 모드의 '크기→점수' 규칙은
        //AbsorbMode가 NetEntity를 통해 사람·봇 모두에게 똑같이 적용한다
        private void HostSendScale()
        {
            if (!IsHost() || id == null)
                return;

            sendTimer += Time.deltaTime;
            if (sendTimer < 1f / scaleSendRate)
                return;
            sendTimer = 0f;

            float s = CurrentScale;
            Color c = ReadVisualColor();

            bool scaleChanged = Mathf.Abs(s - lastSentScale) >= scaleThreshold;
            bool colorChanged = !Approximately(c, lastSentColor);
            if (!scaleChanged && !colorChanged)
                return;

            HostBroadcastState();
        }

        /// <summary>봇의 크기·색·점수를 한 패킷으로 내보낸다.</summary>
        private void HostBroadcastState()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || id == null)
                return;

            float s = CurrentScale;
            Color c = ReadVisualColor();

            lastSentScale = s;
            lastSentColor = c;

            w.Begin(MsgType.BotState);
            w.WriteInt(id.NetId);
            w.WriteFloat(s);
            w.WriteFloat(c.r);
            w.WriteFloat(c.g);
            w.WriteFloat(c.b);
            w.WriteInt(CurrentScore);
            w.End();
            net.Host.Broadcast(w);
        }

        private void FollowScale()
        {
            if (targetScale <= 0f)
                return;
            transform.localScale = Vector3.Lerp(
                transform.localScale, Vector3.one * targetScale, Time.deltaTime * 10f);
        }

        public void ApplyState(float scale, Color color, int score)
        {
            targetScale = scale;
            CurrentScore = score;
            ApplyVisualColor(color);
        }

        private Renderer bodyRenderer;
        private Color lastSentColor = Color.clear;

        private Renderer Rend
        {
            get
            {
                if (bodyRenderer == null)
                    bodyRenderer = GetComponentInChildren<Renderer>(true);
                return bodyRenderer;
            }
        }

        public Color ReadVisualColor()
        {
            return JellyShaderProps.ReadFresnel(Rend);
        }

        //봇도 PlayerColorVisual을 갖고 있다. 호스트에서는 그게 세 프로퍼티를 다 칠하는데
        //여기서 머티리얼에 프레넬만 꽂으면 클라 화면의 본체 색이 초기값에 머문다.
        //같은 컴포넌트에 넘겨 파생 공식을 공유해야 양쪽이 같은 그림이 된다
        private void ApplyVisualColor(Color c)
        {
            if (colorVisual != null)
            {
                colorVisual.ApplyNetworkColor(c);
                return;
            }

            Renderer r = Rend;
            if (r == null)
                return;

            Material m = r.material;

            if (m != null && m.HasProperty(JellyShaderProps.FresnelColorId))
                m.SetColor(JellyShaderProps.FresnelColorId, c);
        }

        private static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f
                && Mathf.Abs(a.g - b.g) < 0.01f
                && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        float CurrentScale
        {
            get
            {
                if (scaleCtrl != null)
                    return scaleCtrl.currentScaleValue;
                return transform.localScale.x;
            }
        }

        /// <summary>
        /// 이 봇을 탈락시킨다 — 전원에게 알리고 내 쪽에도 즉시 반영한다.
        /// 호출자는 NetEntity.HostEliminate 하나뿐이다(사람과 같은 관문).
        /// </summary>
        public void HostEliminate()
        {
            //오프라인 단독 실행에서도 봇은 죽어야 한다(방송만 건너뛴다)
            if (!IsHost() && !NetManager.Offline)
                return;

            AIPlayerMovement brain = id != null ? id.Bot : null;

            if (!NetManager.Offline && id != null)
            {
                w.Begin(MsgType.BotEliminated);
                w.WriteInt(id.NetId);
                w.End();
                NetManager.Instance.Host.Broadcast(w);
            }

            if (brain != null)
                brain.ApplyEliminated();
        }

        /// <summary>
        /// 흡수 연출이 끝난 봇의 몸을 세상에서 치운다. <b>흡수 모드 전용</b>이다.
        ///
        /// ★ 왜 밀치기에는 짝이 없나
        ///   밀치기의 탈락은 '떨어져서 초콜릿에 둥둥 뜬 상태'가 곧 결과 표현이다.
        ///   몸이 남아 있어야 관전 카메라도 보여줄 게 있다. 반대로 흡수는 몸이
        ///   상대에게 빨려 들어가 사라지는 게 연출의 끝이라, 그 시점에 치워야 한다.
        ///   이름에 조건을 적어두지 않으면 "왜 한 모드에서만 부르지?"로 읽힌다.
        /// </summary>
        public void HostDespawnAfterAbsorbed()
        {
            if (!IsHost() || id == null || NetWorld.Instance == null)
                return;
            NetWorld.Instance.HostDespawn(id.NetId);
        }
    }
}
