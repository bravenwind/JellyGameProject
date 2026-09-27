using UnityEngine;

namespace JellyNet
{
    public class LanBotState : MonoBehaviour, INetEntity
    {
        [SerializeField] private float scaleSendRate = 5f;

        [SerializeField] private float scaleThreshold = 0.01f;

        public NetIdentity Identity { get; private set; }
        private PlayerScaleController scaleCtrl;
        private NameTagBillboard nameTag;
        private PlayerColorVisual colorVisual;

        private float sendTimer;
        private float lastSentScale = -1f;
        private float targetScale = -1f;

        private readonly NetWriter w = new NetWriter();

        public string BotName { get; private set; }

        public int CurrentScore { get; private set; }

        private AIPlayerMovement bot;

        private const float ScaleFollowSpeed = 10f;

        private Renderer bodyRenderer;
        private Color lastSentColor = Color.clear;

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

        public int EntityId { get { return Identity != null ? Identity.NetId : 0; } }
        public int OwnerId { get { return Identity != null ? Identity.OwnerId : 0; } }
        public bool IsBot { get { return true; } }
        public string DisplayName { get { return string.IsNullOrEmpty(BotName) ? ("AI 봇 " + EntityId) : BotName; } }
        public float ScaleValue
        {
            get
            {
                if (IsDriver && scaleCtrl != null)
                    return scaleCtrl.CurrentScaleValue;

                return targetScale > 0f ? targetScale : transform.localScale.x;
            }
        }
        public Transform Transform { get { return transform; } }
        public int Score { get { return CurrentScore; } }
        public Color VisualColor { get { return ReadVisualColor(); } }
        public bool IsOutOfPlay { get { return bot != null && bot.IsOutOfPlay; } }

        public bool IsDriver
        {
            get { return Identity != null && Identity.IsMineOrOffline; }
        }

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
            Identity = GetComponent<NetIdentity>();
            bot = GetComponent<AIPlayerMovement>();
            scaleCtrl = GetComponent<PlayerScaleController>();
            nameTag = GetComponentInChildren<NameTagBillboard>(true);
            colorVisual = GetComponentInChildren<PlayerColorVisual>(true);
        }

        private void Start()
        {
            BotName = "AI 봇 " + (Identity != null ? Identity.NetId : 0);
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

        private void HostSendScale()
        {
            if (!IsHost() || Identity == null)
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

        private void HostBroadcastState()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || Identity == null)
                return;

            float s = CurrentScale;
            Color c = ReadVisualColor();

            lastSentScale = s;
            lastSentColor = c;

            w.Begin(MsgType.BotState);
            w.WriteInt(Identity.NetId);
            w.WriteFloat(s);
            w.WriteFloat(c.r);
            w.WriteFloat(c.g);
            w.WriteFloat(c.b);
            w.WriteInt(CurrentScore);
            w.WriteByte(IsEliminatedNow ? (byte)1 : (byte)0);
            w.End();
            net.Broadcast(w);
        }

        private void FollowScale()
        {
            if (targetScale <= 0f)
                return;

            transform.localScale = Vector3.Lerp(
                transform.localScale,
                Vector3.one * targetScale,
                SmoothDamping.Factor(ScaleFollowSpeed, Time.deltaTime));
        }

        private bool IsEliminatedNow { get { return bot != null && bot.IsEliminated; } }

        public void ApplyState(float scale, Color color, int score, bool eliminated)
        {
            targetScale = scale;
            CurrentScore = score;
            ApplyVisualColor(color);

            if (eliminated && bot != null)
                bot.ApplyEliminated();
        }

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
                    return scaleCtrl.CurrentScaleValue;
                return transform.localScale.x;
            }
        }

        public void HostEliminate()
        {
            if (!IsHost() && !NetManager.Offline)
                return;

            AIPlayerMovement brain = Identity != null ? Identity.Bot : null;

            if (brain != null)
                brain.ApplyEliminated();

            if (!NetManager.Offline)
                HostBroadcastState();
        }

        public void HostDespawnAfterAbsorbed()
        {
            if (!IsHost() || Identity == null || NetWorld.Instance == null)
                return;
            NetWorld.Instance.HostDespawn(Identity.NetId);
        }
    }
}
