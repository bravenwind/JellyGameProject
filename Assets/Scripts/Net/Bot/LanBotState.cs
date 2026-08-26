using UnityEngine;

namespace JellyNet
{
    public class LanBotState : MonoBehaviour, INetEntity
    {
        [Header("상태 전송")]
        [Tooltip("초당 몇 번 보낼지. 크기·색·점수는 자주 안 변해서 낮아도 된다.")]
        [SerializeField] private float scaleSendRate = 5f;

        [Tooltip("이만큼 차이 나야 보낸다. 미세 떨림으로 도배되는 걸 막는다.")]
        [SerializeField] private float scaleThreshold = 0.01f;

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

        // ─────────────────────────────────────────────────────────
        //  INetEntity — 밖에서 봇에게 묻는 것들
        // ─────────────────────────────────────────────────────────
        //
        // ★ IsOutOfPlay는 AIPlayerMovement에 있다
        //   봇의 '판 밖' 상태는 두뇌(IsEliminated / IsBeingAbsorbed)가 들고 있어서
        //   여기서 그대로 넘겨준다. 사람 쪽 LanPlayerState는 PlayerFlags를 직접 들고 있는데,
        //   그 비대칭을 **이 한 줄 안에 가둔다** — 밖에서는 둘 다 INetEntity.IsOutOfPlay다.
        //   예전엔 NetEntity·LanScoreboard·AIDetector가 각자 if (IsBot)로 갈랐다.
        public NetIdentity Identity { get { return id; } }
        public int EntityId { get { return id != null ? id.NetId : 0; } }
        public int OwnerId { get { return id != null ? id.OwnerId : 0; } }
        public bool IsBot { get { return true; } }
        public string DisplayName { get { return string.IsNullOrEmpty(BotName) ? ("AI 봇 " + EntityId) : BotName; } }
        /// <summary>
        /// 이 봇의 크기. **읽는 기계에 따라 출처가 다르다.**
        ///
        /// ★ 왜 갈라지나
        ///   봇의 PlayerScaleController는 호스트에서만 돈다 —
        ///   LanPlayerVisual.ApplyGrow가 `if (bot != null && !bot.IsDriver) return;` 으로 막는다.
        ///   막는 이유는 클라에서 FollowScale(절대값 수신)과 ScaleTo(성장 연출)가
        ///   둘 다 transform.localScale을 써서 크기가 튀기 때문이다. 쓰는 쪽을 하나로 둔 것이다.
        ///
        ///   그 결과 클라의 currentScaleValue는 스폰 당시 값에 머문다.
        ///   그걸 그대로 읽으면 클라 순위표의 봇 크기가 안 움직이고,
        ///   AbsorbMode가 '먹을 수 있다'고 잘못 판단해 호스트에 헛요청을 보낸다.
        ///
        ///   그래서 호스트(=구동자)는 논리값을, 클라는 실제로 갱신되는 transform을 읽는다.
        /// </summary>
        public float ScaleValue
        {
            get
            {
                if (IsDriver && scaleCtrl != null)
                    return scaleCtrl.currentScaleValue;

                return transform.localScale.x;
            }
        }
        public Transform Transform { get { return transform; } }
        public int Score { get { return CurrentScore; } }
        public Color VisualColor { get { return ReadVisualColor(); } }
        public bool IsOutOfPlay { get { return bot != null && bot.IsOutOfPlay; } }

        private AIPlayerMovement bot;

        public bool IsDriver
        {
            //봇은 전부 NetWorld가 스폰하므로 id가 없는 봇은 없다
            get { return id != null && id.IsMineOrOffline; }
        }

        //봇의 INetEntity 구현체는 이 컴포넌트다. 등록도 여기서 한다 —
        //AIPlayerMovement는 두뇌라 밖에서 물어보는 창구가 아니다.
        //
        //★ 예전엔 AIPlayerMovement가 EntityRegistry.Bots에 자기를 넣었다.
        //  사람 쪽 짝은 LanPlayerState인데 봇만 두뇌를 등록하니 층이 어긋났고,
        //  밖에서 "사람이든 봇이든 같은 질문"을 할 때마다 목록 두 개를 따로 돌면서
        //  한쪽에만 조건이 빠지는 일이 반복됐다(발판 마모의 IsBeingAbsorbed 누락 등).
        private void OnEnable()
        {
            EntityRegistry.Register(this);
        }

        private void OnDisable()
        {
            EntityRegistry.Unregister(this);
        }

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
            bot = GetComponent<AIPlayerMovement>();
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
            w.WriteByte(IsEliminatedNow ? (byte)1 : (byte)0);
            w.End();
            net.Host.Broadcast(w);
        }

        //호스트가 보내주는 크기로 부드럽게 따라간다. 클라에서만 돈다
        //(호스트는 PlayerScaleController가 직접 몰기 때문에 이 경로를 타지 않는다)
        private const float ScaleFollowSpeed = 10f;

        private void FollowScale()
        {
            if (targetScale <= 0f)
                return;

            //★ 예전엔 t 자리에 Time.deltaTime * 10f 을 그대로 넣었다
            //  Lerp의 결과를 다시 자기 자신에 대입하는 형태라 남은 차이가
            //  (1 - 10·dt)^n 으로 줄어드는데, 여기엔 프레임 수 n이 지수로 들어간다.
            //  → 60fps와 30fps에서 봇 크기가 따라붙는 속도가 달랐다.
            //  같은 파일 옆의 NetTransform.ApplyLerp는 이미 아래 형태를 쓰고 있었다
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                Vector3.one * targetScale,
                SmoothDamping.Factor(ScaleFollowSpeed, Time.deltaTime));
        }

        //호스트의 '이 봇은 탈락했다'를 패킷에 실어 보내기 위한 창구.
        //판정 자체는 두뇌(AIPlayerMovement)가 들고 있다
        private bool IsEliminatedNow { get { return bot != null && bot.IsEliminated; } }

        public void ApplyState(float scale, Color color, int score, bool eliminated)
        {
            targetScale = scale;
            CurrentScore = score;
            ApplyVisualColor(color);

            //★ 탈락은 이제 '사건'이 아니라 '상태'다
            //  예전엔 MsgType.BotEliminated 한 방으로만 알렸다. 일회성이라 그 순간
            //  접속해 있지 않았거나 패킷을 놓친 클라에서는 봇이 영영 안 죽은 것으로 남았다.
            //  (사람 쪽 LanPlayerState는 처음부터 Flags를 상태로 실어 보내고 있었다)
            //  상태로 바꾸면 주기 방송이 알아서 따라잡는다. ApplyEliminated는 첫 줄에
            //  중복 가드가 있어 몇 번 들어와도 한 번만 처리된다
            if (eliminated && bot != null)
                bot.ApplyEliminated();
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

            //먼저 두뇌에 반영해야 아래 방송이 탈락 상태를 실어 나간다
            if (brain != null)
                brain.ApplyEliminated();

            if (!NetManager.Offline)
                HostBroadcastState();
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
