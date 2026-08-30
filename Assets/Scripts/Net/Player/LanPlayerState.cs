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
            //컨트롤러가 없으면 프리팹 크기로 떨어진다 — NetEntity.ScaleOf·AIDetector와 같은 규칙.
            //예전엔 여기만 1f였다. 같은 상황에서 답이 달라 '누가 더 크냐'가 보는 쪽마다 갈렸다
            get { return scale != null ? scale.CurrentScaleValue : transform.localScale.x; }
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
                anim.SetBool(AnimParams.IsMoving, false);

            if (!IsMine)
                return;

            // ★ 내 캐릭터만 물리로 넘긴다
            //   원격 사본은 NetTransform이 위치를 몰고 있다. 거기서 물리까지 켜면
            //   받은 좌표와 물리가 서로를 밀어 캐릭터가 떨린다.
            //   소유자 화면에서 계산한 낙하가 NetTransform을 타고 모두에게 전해진다 —
            //   봇이 호스트에서만 물리를 켜는 것과 같은 규칙이다.
            BeginPhysicsFall(pm);

            if (PlaySFXAudio.Instance != null)
                PlaySFXAudio.Instance.StopWalking();

            if (LanGameFlow.Instance != null)
                LanGameFlow.Instance.ShowLocalGameOver(
                    LanGameFlow.EliminationReason + "\n관전 중...");
        }

        /// <summary>
        /// 탈락 확정을 기다리지 않고 지금 바로 물리로 넘긴다.
        ///
        /// ★ 왜 기다리면 안 되나
        ///   초콜릿에 닿아도 탈락은 호스트 왕복 뒤에 확정된다. 그 사이 CharacterController가
        ///   계속 캐릭터를 몰기 때문에, <b>두께 0.11짜리 얇은 초콜릿 판을 그대로 통과</b>한다.
        ///   빠져나간 뒤에는 트리거 밖이라 부력도 흐름도 못 받아 허공에 멈춰 선다.
        ///   봇은 진입 순간 물리로 바뀌어 그 자리에서 제동이 걸린다 — 사람도 같아야 한다.
        /// </summary>
        public void BeginPhysicsFallNow()
        {
            BeginPhysicsFall(GetComponentInChildren<PlayerMovement>(true));
        }

        /// <summary>
        /// 탈락한 내 캐릭터를 물리 낙하로 전환한다.
        ///
        /// CharacterController는 Rigidbody 물리를 무시하므로 <b>반드시 먼저 꺼야</b> 한다.
        /// 이걸 안 하면 초콜릿의 부력·점성을 못 받아 봇과 다르게 그냥 가라앉는다.
        /// </summary>
        private void BeginPhysicsFall(PlayerMovement pm)
        {
            if (pm != null)
                pm.enabled = false;

            CharacterController cc = pm != null ? pm.Controller : GetComponentInChildren<CharacterController>(true);

            if (cc != null)
                cc.enabled = false;

            PhysicsFall.Begin(gameObject);
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
