using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 플레이어의 게임 상태(점수·탈락·색·이름). NetworkPlayerSync에서 규칙만 추린 것.
    ///
    /// ★ 권위는 호스트에 있다.
    ///   클라는 스스로 점수를 올리거나 탈락을 해제하지 않는다. 통보받아 반영만 한다.
    ///
    /// ★ 왜 셋을 한 메시지로 묶는가
    ///   점수·탈락·색은 모두 '자주 안 바뀌고, 바뀔 때 대개 함께' 바뀐다.
    ///   (젤리를 먹으면 점수↑ + 색 변화, 흡수당하면 탈락 + 점수 정산)
    ///   따로 보내면 메시지 수만 늘고 얻는 게 없다.
    ///
    /// ★ 원본과 달라진 점
    ///   Photon판은 색을 OnPhotonSerializeView로 <b>매 프레임</b> 보냈다.
    ///   하지만 색이 바뀌는 건 젤리를 먹었을 때뿐이라 그건 낭비였다.
    ///   여기서는 <b>바뀔 때만</b> 보낸다.
    /// </summary>
    public class LanPlayerState : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("색을 칠할 렌더러. 비워두면 자식에서 찾는다.")]
        public Renderer targetRenderer;

        [Tooltip("색이 바뀔 때 부드럽게 전환하는 속도. 0이면 즉시.")]
        public float colorLerpSpeed = 6f;

        // ── 상태 (읽기 전용 공개) ──
        public int Score { get; private set; }

        /// <summary>호스트가 내려준 색. 실제 표시색은 <see cref="VisualColor"/>를 쓴다.</summary>
        public Color DisplayColor { get; private set; }

        /// <summary>
        /// 화면에 실제로 보이는 색. 미니맵·인디케이터·리더보드가 이걸 쓴다.
        ///
        /// ★ 왜 DisplayColor를 쓰면 안 되는가
        ///   DisplayColor는 호스트가 HostSetColor/HostReward로 채워주는 값인데,
        ///   그 둘을 부르는 곳이 <b>아무 데도 없다.</b> 젤리를 먹어 색이 섞이는 건
        ///   PlayerColorVisual이 각 클라에서 직접 처리하기 때문이다(GrowEvent 경로).
        ///   그래서 DisplayColor는 영원히 흰색이고, 인디케이터도 흰색으로 나왔다.
        ///
        ///   봇 인디케이터만 색이 나왔던 이유가 이것이다 — 봇은 처음부터
        ///   렌더러의 _FresnelColor를 직접 읽고 있었다. 사람도 같은 방식으로 읽는다.
        /// </summary>
        public Color VisualColor
        {
            get
            {
                if (targetRenderer != null)
                {
                    Material m = targetRenderer.sharedMaterial;   // 인스턴스 복제 방지
                    if (m != null && m.HasProperty(FresnelColor)) return m.GetColor(FresnelColor);
                }
                return DisplayColor;
            }
        }

        static readonly int FresnelColor = Shader.PropertyToID("_FresnelColor");
        public string PlayerName { get; private set; }
        public PlayerFlags Flags { get; private set; }

        public bool IsEliminated { get { return (Flags & PlayerFlags.Eliminated) != 0; } }
        public bool IsAbsorbed { get { return (Flags & PlayerFlags.Absorbed) != 0; } }

        /// <summary>탈락했거나 흡수되어 판 밖인 상태. 판정의 단일 출처.</summary>
        public bool IsOutOfPlay { get { return Flags != PlayerFlags.None; } }

        NetIdentity _id;
        PlayerScaleController _scale;
        Color _shownColor;

        // ═════════════════════════════════════════════
        //  EntityRegistry 연동
        // ═════════════════════════════════════════════
        //
        // ★ 이 컴포넌트가 NetworkPlayerSync의 자리를 그대로 물려받는다.
        //   링 붕괴·AI 탐지·점수판·화면밖 표시가 모두 EntityRegistry.Players를 순회하는데,
        //   등록자가 사라지면 그 기능들이 조용히 전부 멈춘다(에러 없이).
        //   그래서 등록/해제는 반드시 여기 있어야 한다.

        /// <summary>개체를 가리키는 번호. Photon의 photonView.ViewID 자리.</summary>
        public int EntityId { get { return _id != null ? _id.NetId : 0; } }

        /// <summary>내가 조종하는 플레이어인가.</summary>
        public bool IsMine { get { return _id != null && _id.IsMine; } }

        /// <summary>이 플레이어를 조종하는 사람의 번호. Photon의 ActorNumber 자리.</summary>
        public int OwnerId { get { return _id != null ? _id.OwnerId : 0; } }

        /// <summary>현재 크기. AI 탐지·추격 판정이 이 값을 쓴다.</summary>
        public float ScaleValue
        {
            get { return _scale != null ? _scale.currentScaleValue : 1f; }
        }

        void OnEnable() { EntityRegistry.Register(this); }
        void OnDisable() { EntityRegistry.Unregister(this); }

        void Start()
        {
            // ★ 내 닉네임을 알린다.
            //   호스트면 바로 확정하고, 참가자면 호스트에게 보낸다.
            //   스폰 직후에 해야 다른 사람 화면의 이름표가 늦게 뜨지 않는다.
            if (!IsMine || string.IsNullOrEmpty(LanRoomConfig.Nickname)) return;

            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            if (net.IsHost) { HostSetName(LanRoomConfig.Nickname); return; }

            NetWriter w = new NetWriter();
            w.Begin(MsgType.SetMyName);
            w.WriteString(LanRoomConfig.Nickname);
            w.End();
            net.Client.Send(w);
        }

        void Awake()
        {
            _id = GetComponent<NetIdentity>();
            _scale = GetComponentInChildren<PlayerScaleController>(true);
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

            DisplayColor = Color.white;
            _shownColor = Color.white;
            PlayerName = "";
        }

        void Update()
        {
            if (targetRenderer == null) return;
            if (_shownColor == DisplayColor) return;

            if (colorLerpSpeed <= 0f) _shownColor = DisplayColor;
            else
            {
                float t = 1f - Mathf.Exp(-colorLerpSpeed * Time.deltaTime);
                _shownColor = Color.Lerp(_shownColor, DisplayColor, t);
            }
            targetRenderer.material.color = _shownColor;
        }

        // ═════════════════════════════════════════════
        //  호스트 전용 — 상태를 바꾸고 전원에게 알린다
        // ═════════════════════════════════════════════
        public void HostAddScore(int delta)
        {
            if (!IsHost()) return;
            Score += delta;
            HostBroadcast();
        }

        public void HostSetColor(Color c)
        {
            if (!IsHost()) return;
            DisplayColor = c;
            HostBroadcast();
        }

        public void HostSetFlag(PlayerFlags flag, bool on)
        {
            if (!IsHost()) return;

            PlayerFlags next = on ? (Flags | flag) : (Flags & ~flag);
            if (next == Flags) return;      // 안 바뀌면 안 보낸다

            SetFlags(next);
            HostBroadcast();
        }

        // ═════════════════════════════════════════════
        //  탈락 반응 — 전원의 화면에서 같은 일이 일어나야 한다
        // ═════════════════════════════════════════════
        //
        // ★ 왜 플래그를 바꾸는 곳을 한 군데로 모았나
        //   호스트는 HostSetFlag로, 클라는 ApplyState로 플래그가 바뀐다.
        //   반응(조작 정지·애니메이션 끄기·게임오버)을 양쪽에 각각 쓰면
        //   한쪽만 고치는 실수가 생긴다. 전이 감지를 여기 하나로 둔다.
        void SetFlags(PlayerFlags next)
        {
            bool wasOut = IsOutOfPlay;
            Flags = next;
            if (!wasOut && IsOutOfPlay) OnBecameOutOfPlay();
        }

        /// <summary>
        /// 원본 RPC_ChocolateElimination이 하던 일.
        /// 흡수는 별도 연출(LanPlayerVisual.PlayAbsorbed)이 있으므로 여기선 탈락만 다룬다.
        /// </summary>
        void OnBecameOutOfPlay()
        {
            if (IsAbsorbed) return;   // 흡수는 연출 쪽에서 처리

            PlayerMovement pm = GetComponentInChildren<PlayerMovement>(true);
            if (pm != null) pm.enabled = false;

            // ★ FSM을 그냥 끄면 현재 상태의 Exit()가 안 불려서 IsMoving이 true로 남는다.
            //   그러면 죽은 캐릭터가 계속 걷는 애니메이션을 재생한다. 직접 꺼준다.
            Animator anim = pm != null ? pm.jellyAnimator : null;
            if (anim == null) anim = GetComponentInChildren<Animator>(true);
            if (anim != null) anim.SetBool("IsMoving", false);

            if (IsMine)
            {
                if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.StopWalking();
                if (LanGameFlow.Instance != null)
                    LanGameFlow.Instance.ShowLocalGameOver("초콜릿에 빠졌습니다!\n관전 중...");
            }
        }

        /// <summary>
        /// 소유자가 자기 점수를 알린다(PlayerBridge에서 호출).
        ///
        /// ★ 호스트 권위 원칙과 어긋나 보이지만 그렇지 않다.
        ///   점수는 ScoreFromScale(크기)의 순수 함수이고, 크기는 GrowEvent로
        ///   이미 전원에게 동일하게 반영된다. 즉 이 값은 '새 정보'가 아니라
        ///   각 클라가 스스로 계산할 수 있는 값의 캐시다. 그래서 클라가 보내도
        ///   위조 이득이 없다(위조하면 자기 화면 숫자만 틀어진다).
        ///
        ///   호스트면 즉시 전원에게 확정값을 내려보낸다.
        ///   클라면 우선 자기 화면에만 반영한다. (전원 공유 점수판은 아직 미구현 —
        ///    필요해지면 호스트가 모든 LanPlayerVisual.ScaleValue로 직접 계산하면 된다)
        /// </summary>
        public void ReportOwnScore(int score)
        {
            Score = score;
            if (IsHost()) HostBroadcast();
        }

        /// <summary>흡수 보상을 한 번에 — 점수·색을 함께 바꾸고 메시지는 하나만 보낸다.</summary>
        public void HostReward(int scoreDelta, Color newColor)
        {
            if (!IsHost()) return;
            Score += scoreDelta;
            DisplayColor = newColor;
            HostBroadcast();
        }

        public void HostSetName(string name)
        {
            if (!IsHost() || _id == null) return;
            PlayerName = name ?? "";
            if (NetWorld.Instance != null) NetWorld.Instance.BroadcastPlayerName(_id.NetId, PlayerName);
        }

        void HostBroadcast()
        {
            if (_id == null || NetWorld.Instance == null) return;
            NetWorld.Instance.BroadcastPlayerState(_id.NetId, Score, (byte)Flags, DisplayColor);
        }

        static bool IsHost()
        {
            return NetManager.Instance != null && NetManager.Instance.IsHost;
        }

        // ═════════════════════════════════════════════
        //  수신 적용 (NetWorld가 호출)
        // ═════════════════════════════════════════════
        public void ApplyState(int score, byte flags, Color color)
        {
            Score = score;
            DisplayColor = color;
            SetFlags((PlayerFlags)flags);   // 전이 감지가 여기 들어 있다
        }

        public void ApplyName(string name)
        {
            PlayerName = name ?? "";
            gameObject.name = "Player_" + PlayerName + "_net" + (_id != null ? _id.NetId : 0);
        }

        /// <summary>스폰 직후 색을 즉시 맞춘다(전환 연출 없이).</summary>
        public void SnapColorNow()
        {
            _shownColor = DisplayColor;
            if (targetRenderer != null) targetRenderer.material.color = _shownColor;
        }
    }
}
