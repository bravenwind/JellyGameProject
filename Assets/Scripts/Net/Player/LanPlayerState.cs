using UnityEngine;

namespace JellyNet
{
    public class LanPlayerState : MonoBehaviour, INetEntity
    {
        [Header("표시")]
        [Tooltip("색을 칠할 렌더러. 비워두면 자식에서 찾는다.")]
        [SerializeField] private Renderer targetRenderer;

        [Tooltip("색이 바뀔 때 부드럽게 전환하는 속도. 0이면 즉시.")]
        [SerializeField] private float colorLerpSpeed = 6f;

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

        public bool IsAbsorbed { get { return (Flags & PlayerFlags.Absorbed) != 0; } }

        public bool IsOutOfPlay { get { return Flags != PlayerFlags.None; } }

        private NetIdentity id;
        private PlayerScaleController scale;
        private Color shownColor;

        public int EntityId { get { return id != null ? id.NetId : 0; } }

        //Awake에서 캐시해둔 것을 그대로 준다. 밖에서 GetComponent를 다시 부르지 않게
        public NetIdentity Identity { get { return id; } }

        public bool IsMine { get { return id != null && id.IsMine; } }

        //INetEntity — 봇(LanBotState)과 같은 창구로 묻기 위한 것들
        public bool IsBot { get { return false; } }
        public string DisplayName { get { return string.IsNullOrEmpty(PlayerName) ? ("P" + OwnerId) : PlayerName; } }

        public int OwnerId { get { return id != null ? id.OwnerId : 0; } }

        public float ScaleValue
        {
            get { return scale != null ? scale.currentScaleValue : 1f; }
        }

        public Transform Transform { get { return transform; } }

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
            scale = GetComponentInChildren<PlayerScaleController>(true);
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();

            DisplayColor = Color.white;
            shownColor = Color.white;
            PlayerName = "";
        }

        private void Start()
        {
            if (!IsMine)
                return;

            NetManager net = NetManager.Instance;
            if (NetManager.Offline)
                return;

            //닉네임이 비어도 반드시 뭔가는 보낸다. 이름표는 이름이 도착해야 채워지므로
            //(LanPlayerSetup은 플레이스홀더가 보이지 않게 비워둔다) 여기서 조용히
            //빠지면 그 플레이어의 이름표가 영영 빈칸으로 남는다.
            //로비가 빈 닉네임을 막고 있지만 그건 로비의 사정이고, 이 불변식
            //'살아있는 플레이어의 PlayerName은 비지 않는다'는 여기서 지킨다
            string nick = !string.IsNullOrEmpty(LanRoomConfig.Nickname)
                ? LanRoomConfig.Nickname
                : ("P" + OwnerId);

            if (net.IsHost)
            {
                HostSetName(nick);
                return;
            }

            NetWriter w = new NetWriter();
            w.Begin(MsgType.SetMyName);
            w.WriteString(nick);
            w.End();
            net.Client.Send(w);
        }

        private void Update()
        {
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


        //점수를 '어떻게' 정할지는 모드가 안다(밀치기는 더하기, 흡수는 크기에서).
        //여기는 그 결과를 적고 방송하는 일만 한다 — 예전엔 흡수 규칙(크기→점수)이
        //이 안에 Push 가드와 함께 들어 있어서, 모드 전용 규칙이 공통 컴포넌트로 새어 있었다
        public void HostAddScore(int delta)
        {
            if (!IsHost() || delta == 0)
                return;
            Score += delta;
            HostBroadcast();
        }

        public void HostSetScore(int score)
        {
            if (!IsHost() || score == Score)
                return;
            Score = score;
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

            Animator anim = pm != null ? pm.Anim : null;
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

        public void HostSetName(string name)
        {
            if (!IsHost() || id == null)
                return;

            SetName(name);

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

        //이름은 스폰보다 늦게 도착한다. LanPlayerSetup은 이름표를 비워만 두므로
        //여기서 채우지 않으면 그 플레이어의 이름표가 영영 빈칸으로 남는다
        public void SetName(string name)
        {
            PlayerName = name ?? "";
            gameObject.name = "Player_" + PlayerName + "_net" + (id != null ? id.NetId : 0);

            if (IsMine || string.IsNullOrEmpty(PlayerName))
                return;

            NameTagBillboard tag = GetComponentInChildren<NameTagBillboard>(true);
            if (tag != null)
                tag.SetName(PlayerName);
        }

    }
}
