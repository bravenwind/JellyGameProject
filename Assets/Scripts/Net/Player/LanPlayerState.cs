using UnityEngine;

namespace JellyNet
{
    public class LanPlayerState : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("색을 칠할 렌더러. 비워두면 자식에서 찾는다.")]
        public Renderer targetRenderer;

        [Tooltip("색이 바뀔 때 부드럽게 전환하는 속도. 0이면 즉시.")]
        public float colorLerpSpeed = 6f;

        public int Score { get; private set; }

        public Color DisplayColor { get; private set; }

        public Color VisualColor
        {
            get
            {
                if (targetRenderer == null)
                    return DisplayColor;

                Material m = targetRenderer.sharedMaterial;

                if (m == null || !m.HasProperty(JellyShaderProps.FresnelColorId))
                    return DisplayColor;

                return m.GetColor(JellyShaderProps.FresnelColorId);
            }
        }

        public string PlayerName { get; private set; }
        public PlayerFlags Flags { get; private set; }

        public bool IsEliminated { get { return (Flags & PlayerFlags.Eliminated) != 0; } }
        public bool IsAbsorbed { get { return (Flags & PlayerFlags.Absorbed) != 0; } }

        public bool IsOutOfPlay { get { return Flags != PlayerFlags.None; } }

        private NetIdentity id;
        private PlayerScaleController scale;
        private Color shownColor;

        public int EntityId { get { return id != null ? id.NetId : 0; } }

        public bool IsMine { get { return id != null && id.IsMine; } }

        public int OwnerId { get { return id != null ? id.OwnerId : 0; } }

        public float ScaleValue
        {
            get { return scale != null ? scale.currentScaleValue : 1f; }
        }

        private void OnEnable() { EntityRegistry.Register(this); }
        private void OnDisable() { EntityRegistry.Unregister(this); }

        private void Start()
        {
            if (!IsMine || string.IsNullOrEmpty(LanRoomConfig.Nickname))
                return;

            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None)
                return;

            if (net.IsHost)
            {
                HostSetName(LanRoomConfig.Nickname);
                return;
            }

            NetWriter w = new NetWriter();
            w.Begin(MsgType.SetMyName);
            w.WriteString(LanRoomConfig.Nickname);
            w.End();
            net.Client.Send(w);
        }

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
            scale = GetComponentInChildren<PlayerScaleController>(true);
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();

            DisplayColor = Color.white;
            shownColor = Color.white;
            PlayerName = "";
        }

        private float scoreTimer;

        private void HostRecomputeScore()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;
            if (DataManager.Instance == null)
                return;

            if (LanGameFlow.Instance != null
                && LanGameFlow.Instance.mode == GameModeType.Push) return;

            scoreTimer += Time.deltaTime;
            if (scoreTimer < 0.25f)
                return;
            scoreTimer = 0f;

            int s = DataManager.Instance.ScoreFromScale(ScaleValue);
            if (s == Score)
                return;

            Score = s;
            HostBroadcast();
        }

        private void Update()
        {
            HostRecomputeScore();

            if (targetRenderer == null)
                return;
            if (shownColor == DisplayColor)
                return;

            if (colorLerpSpeed <= 0f)
                shownColor = DisplayColor;
            else
            {
                float t = 1f - Mathf.Exp(-colorLerpSpeed * Time.deltaTime);
                shownColor = Color.Lerp(shownColor, DisplayColor, t);
            }
            targetRenderer.material.color = shownColor;
        }

        public void HostAddScore(int delta)
        {
            if (!IsHost())
                return;
            Score += delta;
            HostBroadcast();
        }

        public void HostSetColor(Color c)
        {
            if (!IsHost())
                return;
            DisplayColor = c;
            HostBroadcast();
        }

        public void HostSetFlag(PlayerFlags flag, bool on)
        {
            if (!IsHost())
                return;

            PlayerFlags next = on ? (Flags | flag) : (Flags & ~flag);
            if (next == Flags)
                return;

            SetFlags(next);
            HostBroadcast();
        }

        private void SetFlags(PlayerFlags next)
        {
            bool wasOut = IsOutOfPlay;
            Flags = next;
            if (!wasOut && IsOutOfPlay)
                OnBecameOutOfPlay();
        }

        private void OnBecameOutOfPlay()
        {
            if (IsAbsorbed)
                return;

            PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
            if (pm != null)
                pm.enabled = false;

            Animator anim = pm != null ? pm.jellyAnimator : null;
            if (anim == null)
                anim = GetComponentInChildren<Animator>(true);
            if (anim != null)
                anim.SetBool("IsMoving", false);

            if (IsMine)
            {
                if (PlaySFXAudio.Instance != null)
                    PlaySFXAudio.Instance.StopWalking();
                if (LanGameFlow.Instance != null)
                    LanGameFlow.Instance.ShowLocalGameOver(
                        LanGameFlow.EliminationReason + "\n관전 중...");
            }
        }

        public void ReportOwnScore(int score)
        {
            Score = score;
            if (IsHost())
                HostBroadcast();
        }

        public void HostReward(int scoreDelta, Color newColor)
        {
            if (!IsHost())
                return;
            Score += scoreDelta;
            DisplayColor = newColor;
            HostBroadcast();
        }

        public void HostSetName(string name)
        {
            if (!IsHost() || id == null)
                return;
            PlayerName = name ?? "";
            if (NetWorld.Instance != null)
                NetWorld.Instance.BroadcastPlayerName(id.NetId, PlayerName);
        }

        private void HostBroadcast()
        {
            if (id == null || NetWorld.Instance == null)
                return;
            NetWorld.Instance.BroadcastPlayerState(id.NetId, Score, (byte)Flags, DisplayColor);
        }

        private static bool IsHost()
        {
            return NetManager.Instance != null && NetManager.Instance.IsHost;
        }

        public void ApplyState(int score, byte flags, Color color)
        {
            Score = score;
            DisplayColor = color;
            SetFlags((PlayerFlags)flags);
        }

        public void ApplyName(string name)
        {
            PlayerName = name ?? "";
            gameObject.name = "Player_" + PlayerName + "_net" + (id != null ? id.NetId : 0);
        }

        public void SnapColorNow()
        {
            shownColor = DisplayColor;
            if (targetRenderer != null)
                targetRenderer.material.color = shownColor;
        }
    }
}
