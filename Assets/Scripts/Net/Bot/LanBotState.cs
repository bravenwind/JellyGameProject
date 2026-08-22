using UnityEngine;

namespace JellyNet
{
    public class LanBotState : MonoBehaviour
    {
        [Header("크기 전송")]
        [Tooltip("초당 몇 번 보낼지. 크기는 자주 안 변해서 낮아도 된다.")]
        public float scaleSendRate = 5f;

        [Tooltip("이만큼 차이 나야 보낸다. 미세 떨림으로 도배되는 걸 막는다.")]
        public float scaleThreshold = 0.01f;

        private NetIdentity id;
        private PlayerScaleController scaleCtrl;
        private AIPlayerMovement bot;
        private NameTagBillboard nameTag;
        private PlayerColorVisual colorVisual;

        private float sendTimer;
        private float lastSentScale = -1f;
        private float targetScale = -1f;

        private readonly NetWriter w = new NetWriter();

        public string BotName { get; private set; }

        public int CurrentScore { get; private set; }

        public void HostAddScore(int delta)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;
            CurrentScore += delta;
        }

        public bool IsDriver
        {
            //봇은 전부 NetWorld가 스폰하므로 id가 없는 봇은 없다
            get { return id != null && id.IsMineOrOffline; }
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

        private void HostSendScale()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || id == null)
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

            lastSentScale = s;
            lastSentColor = c;

            w.Begin(MsgType.BotState);
            w.WriteInt(id.NetId);
            w.WriteFloat(s);
            w.WriteFloat(c.r);
            w.WriteFloat(c.g);
            w.WriteFloat(c.b);
            w.End();
            net.Host.Broadcast(w);

            if (DataManager.Instance != null && !LanGameFlow.IsMode(GameModeType.Push))
                CurrentScore = DataManager.Instance.ScoreFromScale(s);
        }

        private void FollowScale()
        {
            if (targetScale <= 0f)
                return;
            transform.localScale = Vector3.Lerp(
                transform.localScale, Vector3.one * targetScale, Time.deltaTime * 10f);
        }

        public void ApplyState(float scale, Color color)
        {
            targetScale = scale;
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

        public void HostBroadcastEliminated()
        {
            NetManager net = NetManager.Instance;
            if (net == null || id == null)
                return;

            if (NetManager.Offline)
                return;
            if (!net.IsHost)
                return;

            w.Begin(MsgType.BotEliminated);
            w.WriteInt(id.NetId);
            w.End();
            net.Host.Broadcast(w);
        }

        public void HostDespawnSelf()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || id == null || NetWorld.Instance == null)
                return;
            NetWorld.Instance.HostDespawn(id.NetId);
        }
    }
}
