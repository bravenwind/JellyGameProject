using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// AI 봇의 네트워크 표현. AIPlayerSync가 하던 일을 대신한다.
    ///
    /// ═══════════════════════════════════════════════════════
    ///  ★ 원본과 무엇이 달라졌나
    /// ═══════════════════════════════════════════════════════
    ///
    ///  Photon판 AIPlayerSync는 봇의 이름·점수·크기·색을 전부
    ///  <b>룸 커스텀 프로퍼티</b>에 넣었다("Bot17_Score" 같은 문자열 키).
    ///  이유는 하나였다 — 씬을 전환해도 결과 화면이 그 값을 읽어야 했기 때문.
    ///
    ///  그 방식의 대가가 컸다:
    ///    · 봇 한 마리가 성장할 때마다 룸 전체에 프로퍼티 갱신이 방송된다
    ///    · 키가 문자열이라 오타가 컴파일에 안 걸린다
    ///    · 봇이 죽으면 키를 일일이 null로 지워야 한다(ClearBotProperties)
    ///
    ///  LAN에서는 그럴 이유가 없다. 봇은 호스트가 굴리고, 호스트가 결과를
    ///  계산해서 내려보내면 된다. 그래서 이름·점수는 <b>로컬 값</b>으로 두고,
    ///  네트워크로 보내는 건 각 클라가 스스로 알 수 없는 것 하나뿐이다 — 크기.
    ///
    /// ═══════════════════════════════════════════════════════
    ///  ★ 무엇을 보내고 무엇을 안 보내는가
    /// ═══════════════════════════════════════════════════════
    ///
    ///    위치·회전   → NetTransform이 이미 한다 (봇은 호스트 소유)
    ///    애니메이션  → LanPlayerVisual의 AnimState가 이미 한다
    ///    크기       → 여기서 보낸다 ← 이것만 남는다
    ///    탈락       → 여기서 보낸다 (사건이라 한 번만)
    ///
    ///  크기를 '사건'이 아니라 '절대값'으로 보내는 건 플레이어와 반대인데,
    ///  의도한 것이다. 플레이어는 자기 성장의 원인(젤리를 먹음)을 각 클라가
    ///  알지만, 봇의 성장은 호스트만 아는 판정의 결과라 사건으로 쪼개봐야
    ///  받는 쪽이 재현할 게 없다. 원본도 같은 이유로 스케일을 스트림했다.
    /// </summary>
    public class LanBotSync : MonoBehaviour
    {
        [Header("크기 전송")]
        [Tooltip("초당 몇 번 보낼지. 크기는 자주 안 변해서 낮아도 된다.")]
        public float scaleSendRate = 5f;

        [Tooltip("이만큼 차이 나야 보낸다. 미세 떨림으로 도배되는 걸 막는다.")]
        public float scaleThreshold = 0.01f;

        NetIdentity _id;
        PlayerScaleController _scaleCtrl;
        AIPlayerMovement _bot;
        NameTagBillboard _nameTag;

        float _sendTimer;
        float _lastSentScale = -1f;
        float _targetScale = -1f;

        readonly NetWriter _w = new NetWriter();

        /// <summary>이 봇의 표시 이름.</summary>
        public string BotName { get; private set; }

        /// <summary>점수. 호스트에서만 갱신되고 결과 계산에 쓰인다.</summary>
        public int CurrentScore { get; private set; }

        /// <summary>호스트가 이 봇을 굴리는가. 접속이 없으면(오프라인) 참.</summary>
        public bool IsDriver
        {
            get { return _id == null || _id.IsMineOrOffline; }
        }

        void Awake()
        {
            _id = GetComponent<NetIdentity>();
            _bot = GetComponent<AIPlayerMovement>();
            _scaleCtrl = GetComponent<PlayerScaleController>();
            _nameTag = GetComponentInChildren<NameTagBillboard>(true);
        }

        void Start()
        {
            // ★ 이름은 NetId로 짓는다.
            //   원본은 photonView.ViewID를 썼고 Start()에서 읽으면 0이 나오는 버그가 있어
            //   IPunInstantiateMagicCallback까지 동원했다. NetWorld는 Instantiate 직후
            //   동기 코드로 NetId를 넣어주므로 Start 시점엔 항상 유효하다.
            BotName = "AI 봇 " + (_id != null ? _id.NetId : 0);
            gameObject.name = "Bot_" + BotName;

            if (_nameTag != null)
            {
                _nameTag.SetName(BotName);
                _nameTag.ApplyRoleColor(NameTagRole.Bot);
            }
        }

        // ═════════════════════════════════════════════
        //  크기
        // ═════════════════════════════════════════════
        void Update()
        {
            if (IsDriver) HostSendScale();
            else FollowScale();
        }

        void HostSendScale()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || _id == null) return;

            _sendTimer += Time.deltaTime;
            if (_sendTimer < 1f / scaleSendRate) return;
            _sendTimer = 0f;

            float s = CurrentScale;
            Color c = ReadVisualColor();

            bool scaleChanged = Mathf.Abs(s - _lastSentScale) >= scaleThreshold;
            bool colorChanged = !Approximately(c, _lastSentColor);
            if (!scaleChanged && !colorChanged) return;   // 안 바뀌면 안 보낸다

            _lastSentScale = s;
            _lastSentColor = c;

            _w.Begin(MsgType.BotState);
            _w.WriteInt(_id.NetId);
            _w.WriteFloat(s);
            _w.WriteFloat(c.r);
            _w.WriteFloat(c.g);
            _w.WriteFloat(c.b);
            _w.End();
            net.Host.Broadcast(_w);

            // 점수는 크기에서 계산된다 — 따로 보낼 필요가 없다
            if (DataManager.Instance != null)
                CurrentScore = DataManager.Instance.ScoreFromScale(s);
        }

        /// <summary>원격: 받은 크기로 부드럽게 맞춘다.</summary>
        void FollowScale()
        {
            if (_targetScale <= 0f) return;
            transform.localScale = Vector3.Lerp(
                transform.localScale, Vector3.one * _targetScale, Time.deltaTime * 10f);
        }

        public void ApplyState(float scale, Color color)
        {
            _targetScale = scale;
            ApplyVisualColor(color);
        }

        // ═════════════════════════════════════════════
        //  색
        // ═════════════════════════════════════════════
        //
        // ★ 왜 색까지 보내야 하는가
        //   사람 플레이어의 색은 각 클라가 스스로 계산한다 — "무슨 젤리를 먹었다"는
        //   사건이 전원에게 전달되고, PlayerColorVisual이 같은 혼합을 재현하기 때문이다.
        //   그런데 봇은 호스트에서만 젤리를 먹는다(원격은 PlayerAbsorber가 꺼져 있다).
        //   그래서 클라에는 재현할 사건 자체가 없고, 봇은 영원히 초기 색으로 남는다.
        //   결과를 그대로 내려보내는 수밖에 없다.

        static readonly int FresnelColor = Shader.PropertyToID("_FresnelColor");
        Renderer _renderer;
        Color _lastSentColor = Color.clear;

        Renderer Rend
        {
            get
            {
                if (_renderer == null) _renderer = GetComponentInChildren<Renderer>(true);
                return _renderer;
            }
        }

        public Color ReadVisualColor()
        {
            Renderer r = Rend;
            if (r == null) return Color.white;
            Material m = r.sharedMaterial;      // 읽기 전용 — 인스턴스 복제를 피한다
            if (m == null || !m.HasProperty(FresnelColor)) return Color.white;
            return m.GetColor(FresnelColor);
        }

        void ApplyVisualColor(Color c)
        {
            Renderer r = Rend;
            if (r == null) return;

            // 여기서는 반드시 .material(인스턴스)을 써야 한다.
            // sharedMaterial에 쓰면 그 머티리얼을 공유하는 봇이 전부 같은 색이 된다.
            Material m = r.material;
            if (m != null && m.HasProperty(FresnelColor)) m.SetColor(FresnelColor, c);
        }

        static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f
                && Mathf.Abs(a.g - b.g) < 0.01f
                && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        float CurrentScale
        {
            get
            {
                if (_scaleCtrl != null) return _scaleCtrl.currentScaleValue;
                return transform.localScale.x;
            }
        }

        // ═════════════════════════════════════════════
        //  탈락
        // ═════════════════════════════════════════════
        //
        // ★ 왜 사건으로 보내는가
        //   탈락은 초콜릿에 닿았거나 발판에서 떨어졌을 때 <b>호스트만</b> 판정한다.
        //   그런데 결과(에이전트 정지·이름표 숨김·애니메이션 정지)는 전원의 화면에
        //   반영돼야 한다. 원본의 RPC_OnEliminated(All)와 같은 구조다.

        public void HostBroadcastEliminated()
        {
            NetManager net = NetManager.Instance;
            if (net == null || _id == null) return;

            if (net.CurrentMode == NetManager.Mode.None) return;   // 오프라인이면 로컬만
            if (!net.IsHost) return;

            _w.Begin(MsgType.BotEliminated);
            _w.WriteInt(_id.NetId);
            _w.End();
            net.Host.Broadcast(_w);
        }

        /// <summary>흡수당해 사라질 때 호스트가 오브젝트를 회수한다.</summary>
        public void HostDespawnSelf()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || _id == null || NetWorld.Instance == null) return;
            NetWorld.Instance.HostDespawn(_id.NetId);
        }
    }
}
