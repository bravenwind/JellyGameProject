using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;
using TMPro;

namespace JellyNet
{
    /// <summary>
    /// Main 씬의 로비. LobbyController(Photon판)의 연출을 그대로 쓰되 소켓으로 동작한다.
    ///
    /// ═══════════════════════════════════════════════════════
    ///  화면 흐름과 연출
    /// ═══════════════════════════════════════════════════════
    ///
    ///    닉네임 ─[확인]→ 닉네임이 <b>왼쪽으로 빠지고</b> 방 선택이 <b>오른쪽에서 팝</b>
    ///                     ├─[방 만들기]→ 방 옵션    (가운데 팝)
    ///                     └─[방 참가]  → 주소 입력  (가운데 팝)
    ///                                        ↓
    ///                     매칭 패널 (화면을 덮는 오버레이, 스케일 팝)
    ///                                        ↓
    ///                              3 · 2 · 1 → 로딩 씬 → 게임 씬
    ///
    /// ═══════════════════════════════════════════════════════
    ///  ★ '매칭'이라 부르지만 매칭이 아니다
    /// ═══════════════════════════════════════════════════════
    ///
    ///  Photon은 클라우드가 방 목록을 들고 있어 모르는 사람과도 붙여줬다.
    ///  LAN에는 중개자가 없다 — 주소를 아는 사람끼리만 모인다.
    ///  그래서 여기서 '매칭'은 "정한 인원이 다 모일 때까지 기다린다"는 뜻이다.
    ///
    ///  총원 = 사람 + AI 이므로 기다려야 하는 사람은 (총원 − AI)명이다.
    ///  AI 3 · 총원 4로 두면 <b>혼자서도 바로 시작된다</b> — 테스트에 편하도록 의도한 것이다.
    ///
    ///  ★ 원본의 '가짜 카운터'는 뺐다.
    ///    Photon판은 실제 인원과 무관하게 숫자를 올려 붐비는 것처럼 보여줬다.
    ///    아는 사람끼리 IP로 모이는 판에서는 그 거짓말이 오히려 방해가 된다
    ///    ("2/4인데 왜 시작을 안 하지?"). 실제 접속 수만 보여준다.
    /// </summary>
    public class LanLobby : MonoBehaviour
    {
        public static LanLobby Instance { get; private set; }

        // ─────────────────────────────────────────────
        [Header("① 닉네임")]
        public RectTransform nicknamePanel;
        public TMP_InputField nicknameInput;
        public GameObject nicknameWarningText;
        public int nicknameMaxLength = 10;

        [Header("② 방 선택 (오른쪽 팝)")]
        public RectTransform roomChoicePanel;

        [Header("③-A 방 옵션 (가운데 팝)")]
        public RectTransform hostOptionPanel;
        [Tooltip("0 = 흡수, 1 = 밀치기. 드롭다운 항목 순서를 이렇게 맞춰주세요.")]
        public TMP_Dropdown modeDropdown;
        public TMP_InputField portInput;
        public TMP_InputField totalPlayersInput;
        public TMP_InputField aiCountInput;

        [Header("③-B 방 참가 (가운데 팝)")]
        public RectTransform joinPanel;
        [Tooltip("\"ip:port 형식으로 입력\" 안내 문구.")]
        public TMP_Text joinHintText;
        public TMP_InputField joinAddressInput;

        [Header("④ 매칭 (화면 덮는 오버레이)")]
        public RectTransform matchingPanel;
        public TMP_Text matchingStatusText;      // "참가자를 기다리는 중…"
        public TMP_Text currentPlayerCountText;  // "2 / 4명"
        public TMP_Text roomAddressText;         // 방장에게 보여줄 접속 주소
        public GameObject cancelMatchingButton;

        [Header("카운트다운")]
        public TMP_Text countdownText;
        public TMP_Text gameStartText;

        [Header("공통")]
        public TMP_Text statusText;

        // ─────────────────────────────────────────────
        [Header("애니메이션 (LobbyController와 같은 값)")]
        [SerializeField] Vector2 nicknameLeftPos = new Vector2(-400f, 0f);
        [Tooltip("켜면 방 선택 패널을 아래 좌표로 옮겨서 띄운다. 끄면 씬에 배치한 자리를 쓴다.")]
        [SerializeField] bool overrideRoomChoicePos = false;
        [SerializeField] Vector2 roomChoiceRightPos = new Vector2(400f, 0f);
        [SerializeField] float slideDuration = 0.45f;
        [SerializeField] Ease slideEase = Ease.OutCubic;
        [SerializeField] float popDelay = 0.2f;
        [SerializeField] float popDuration = 0.35f;
        [SerializeField] Ease popEase = Ease.OutBack;
        [SerializeField] float matchingCompleteSlideY = 60f;

        [Header("씬")]
        public string gameSceneAbsorb = "Game_io_AbsorbMode";
        public string gameScenePush = "Game_io_PushMode";
        public string loadingScene = "Loading";

        [Header("기본값")]
        public int defaultPort = NetConfig.DefaultPort;
        public int defaultTotalPlayers = 4;
        public int defaultAiCount = 2;
        public float countdownSeconds = 3f;

        // ─────────────────────────────────────────────
        Vector2 _nicknameOriginPos;
        Vector2 _roomChoiceOriginPos;
        Vector2 _matchingStatusOriginPos;

        float _countdown = -1f;
        bool _launching;
        bool _matching;
        Coroutine _dots;
        readonly NetWriter _w = new NetWriter();

        // ═════════════════════════════════════════════
        void Awake() { Instance = this; }

        void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
            {
                Debug.LogError("[로비] NetManager가 없습니다. Main 씬에 NetManager를 올려주세요.");
                return;
            }

            net.OnClientMessage += HandleClientMessage;
            net.OnPeerJoined += HandlePeerChanged;
            net.OnPeerLeft += HandlePeerChanged;
            net.OnDisconnected += HandleDisconnected;

            // 이전 판의 잔재를 지운다
            LanScoreboard.Clear();
            LanRoomConfig.Clear();
            ClearChosenMode();

            CacheOriginPositions();
            ResetPanels();
            FillDefaults();
        }

        void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net != null)
            {
                net.OnClientMessage -= HandleClientMessage;
                net.OnPeerJoined -= HandlePeerChanged;
                net.OnPeerLeft -= HandlePeerChanged;
                net.OnDisconnected -= HandleDisconnected;
            }
            if (Instance == this) Instance = null;
        }

        void CacheOriginPositions()
        {
            if (nicknamePanel != null) _nicknameOriginPos = nicknamePanel.anchoredPosition;
            if (roomChoicePanel != null) _roomChoiceOriginPos = roomChoicePanel.anchoredPosition;
            if (matchingStatusText != null)
                _matchingStatusOriginPos = matchingStatusText.rectTransform.anchoredPosition;
        }

        /// <summary>
        /// 팝으로 등장할 패널들은 <b>스케일 0으로 접어둔 채</b> 꺼둔다.
        /// 켜진 상태로 두면 첫 프레임에 원래 크기로 한 번 번쩍인 뒤 줄어든다.
        /// </summary>
        void ResetPanels()
        {
            Fold(roomChoicePanel);
            Fold(hostOptionPanel);
            Fold(joinPanel);
            Fold(matchingPanel);

            if (nicknamePanel != null)
            {
                nicknamePanel.anchoredPosition = _nicknameOriginPos;
                nicknamePanel.gameObject.SetActive(false);
            }

            if (nicknameWarningText != null) nicknameWarningText.SetActive(false);
            if (countdownText != null) countdownText.gameObject.SetActive(false);
            if (gameStartText != null) gameStartText.gameObject.SetActive(false);
            if (joinHintText != null)
                joinHintText.text = "ip:port 형식으로 입력  (예: 192.168.0.5:7777)";
        }

        static void Fold(RectTransform rt)
        {
            if (rt == null) return;
            rt.localScale = Vector3.zero;
            rt.gameObject.SetActive(false);
        }

        void FillDefaults()
        {
            if (portInput != null && string.IsNullOrEmpty(portInput.text))
                portInput.text = defaultPort.ToString();
            if (totalPlayersInput != null && string.IsNullOrEmpty(totalPlayersInput.text))
                totalPlayersInput.text = defaultTotalPlayers.ToString();
            if (aiCountInput != null && string.IsNullOrEmpty(aiCountInput.text))
                aiCountInput.text = defaultAiCount.ToString();
            if (nicknameInput != null && string.IsNullOrEmpty(nicknameInput.text))
                nicknameInput.text = "플레이어" + Random.Range(100, 1000);
        }

        void Status(string msg) { if (statusText != null) statusText.text = msg; }

        // ═════════════════════════════════════════════
        //  연출 헬퍼
        // ═════════════════════════════════════════════
        void Pop(RectTransform rt, Vector2? at = null, float delay = 0f)
        {
            if (rt == null) return;

            rt.gameObject.SetActive(true);
            rt.localScale = Vector3.zero;
            if (at.HasValue) rt.anchoredPosition = at.Value;

            rt.DOScale(Vector3.one, popDuration)
              .SetEase(popEase).SetDelay(delay).SetUpdate(true);
        }

        void Unpop(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeSelf) return;

            rt.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack).SetUpdate(true)
              .OnComplete(() => rt.gameObject.SetActive(false));
        }

        void SlideNicknameOut()
        {
            if (nicknamePanel == null) return;
            nicknamePanel.DOAnchorPos(nicknameLeftPos, slideDuration)
                         .SetEase(slideEase).SetUpdate(true);
        }

        void SlideNicknameBack()
        {
            if (nicknamePanel == null) return;
            nicknamePanel.gameObject.SetActive(true);
            nicknamePanel.DOAnchorPos(_nicknameOriginPos, slideDuration)
                         .SetEase(slideEase).SetUpdate(true);
        }

        // ═════════════════════════════════════════════
        //  버튼
        // ═════════════════════════════════════════════

        /// <summary>① 닉네임 확인 → 닉네임 왼쪽으로, 방 선택 오른쪽에서 팝.</summary>
        public void OnClickNicknameConfirm()
        {
            string nick = nicknameInput != null ? nicknameInput.text.Trim() : "";

            if (string.IsNullOrEmpty(nick) || nick.Length > nicknameMaxLength)
            {
                if (nicknameWarningText != null) nicknameWarningText.SetActive(true);
                Status("닉네임은 1~" + nicknameMaxLength + "자로 입력해주세요.");
                return;
            }

            if (nicknameWarningText != null) nicknameWarningText.SetActive(false);
            LanRoomConfig.Nickname = nick;
            Status("");

            SlideNicknameOut();

            // 오른쪽 자리에서 팝. overrideRoomChoicePos가 꺼져 있으면
            // 씬에 배치해 둔 원래 위치를 그대로 쓴다(디자이너가 정한 자리).
            Pop(roomChoicePanel,
                overrideRoomChoicePos ? (Vector2?)roomChoiceRightPos : _roomChoiceOriginPos,
                popDelay);
        }

        /// <summary>②-A 방 만들기 → 옵션 패널 가운데 팝.</summary>
        public void OnClickCreateRoom()
        {
            Unpop(roomChoicePanel);
            Pop(hostOptionPanel, null, popDelay);
            Status("");
        }

        /// <summary>②-B 방 참가 → 주소 입력 가운데 팝.</summary>
        public void OnClickJoinRoom()
        {
            Unpop(roomChoicePanel);
            Pop(joinPanel, null, popDelay);
            Status("");
        }

        /// <summary>③-A 매칭 시작 — 방을 열고 인원이 찰 때까지 기다린다.</summary>
        public void OnClickStartMatching()
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;

            int port = ParseInt(portInput, defaultPort);
            int total = ParseInt(totalPlayersInput, defaultTotalPlayers);
            int ai = ParseInt(aiCountInput, defaultAiCount);
            GameModeType mode = (modeDropdown != null && modeDropdown.value == 1)
                ? GameModeType.Push : GameModeType.Absorb;

            if (total < 1) { Status("총 플레이어 수는 1명 이상이어야 합니다."); return; }
            if (ai >= total)
            {
                Status("AI 수는 총 인원보다 적어야 합니다 (사람이 최소 한 명은 있어야 합니다).");
                return;
            }

            LanRoomConfig.Set(mode, total, ai);

            net.port = port;
            net.StartHost();

            if (roomAddressText != null)
                roomAddressText.text = LocalAddress() + " : " + port;

            Unpop(hostOptionPanel);
            OpenMatching("참가자를 기다리는 중");
        }

        /// <summary>③-B 입장 — ip:port를 파싱해 접속한다.</summary>
        public void OnClickConnect()
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;

            string raw = joinAddressInput != null ? joinAddressInput.text.Trim() : "";
            if (string.IsNullOrEmpty(raw)) { Status("주소를 입력해주세요."); return; }

            string ip; int port;
            if (!TryParseAddress(raw, out ip, out port))
            {
                Status("형식이 올바르지 않습니다. ip:port 로 입력해주세요. (예: 192.168.0.5:7777)");
                return;
            }

            net.joinIp = ip;
            net.port = port;
            net.JoinHost();

            if (roomAddressText != null) roomAddressText.text = ip + " : " + port;

            Unpop(joinPanel);
            OpenMatching("방장을 기다리는 중");
        }

        void OpenMatching(string label)
        {
            _matching = true;
            Status("");

            if (cancelMatchingButton != null) cancelMatchingButton.SetActive(true);
            if (matchingStatusText != null)
            {
                matchingStatusText.rectTransform.anchoredPosition = _matchingStatusOriginPos;
                matchingStatusText.text = label;
            }

            Pop(matchingPanel, null, popDelay);

            if (_dots != null) StopCoroutine(_dots);
            _dots = StartCoroutine(AnimateDots(label));

            UpdatePlayerCountUI();
        }

        /// <summary>매칭 취소 / 뒤로.</summary>
        public void OnCancelMatchingClicked()
        {
            NetManager net = NetManager.Instance;
            if (net != null && net.CurrentMode != NetManager.Mode.None) net.Shutdown();

            _matching = false;
            _countdown = -1f;
            _launching = false;

            if (_dots != null) { StopCoroutine(_dots); _dots = null; }
            if (countdownText != null) countdownText.gameObject.SetActive(false);

            Unpop(matchingPanel);
            Pop(roomChoicePanel, null, popDelay);
            Status("");
        }

        /// <summary>옵션/주소 패널에서 방 선택으로 되돌아가기.</summary>
        public void OnClickBack()
        {
            Unpop(hostOptionPanel);
            Unpop(joinPanel);
            Pop(roomChoicePanel, null, popDelay);
            Status("");
        }

        /// <summary>방 선택에서 닉네임으로 되돌아가기.</summary>
        public void OnClickBackToNickname()
        {
            Unpop(roomChoicePanel);
            SlideNicknameBack();
            Status("");
        }

        // ═════════════════════════════════════════════
        //  주소 파싱
        // ═════════════════════════════════════════════
        //
        // ★ 포트를 안 적어도 받아준다.
        //   "192.168.0.5"만 입력하는 사람이 반드시 있다. 거절하는 것보다
        //   기본 포트로 붙여주는 편이 낫다(방장도 대개 기본값을 쓴다).
        public static bool TryParseAddress(string raw, out string ip, out int port)
        {
            ip = raw;
            port = NetConfig.DefaultPort;

            int colon = raw.LastIndexOf(':');
            if (colon < 0) return raw.Length > 0;

            ip = raw.Substring(0, colon).Trim();
            string portPart = raw.Substring(colon + 1).Trim();

            if (ip.Length == 0) return false;
            if (!int.TryParse(portPart, out port)) return false;
            return port > 0 && port <= 65535;
        }

        static int ParseInt(TMP_InputField f, int fallback)
        {
            int v;
            return (f != null && int.TryParse(f.text, out v)) ? v : fallback;
        }

        static string LocalAddress()
        {
            try
            {
                foreach (var a in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return a.ToString();
            }
            catch { }
            return "127.0.0.1";
        }

        // ═════════════════════════════════════════════
        //  대기 → 카운트다운 → 입장
        // ═════════════════════════════════════════════
        void HandlePeerChanged(NetHost.Peer peer)
        {
            UpdatePlayerCountUI();
            PlayJoinPop();
        }

        void HandleDisconnected()
        {
            if (_launching) return;   // 씬 전환 중의 정리는 무시
            Status("연결이 끊어졌습니다.");
            OnCancelMatchingClicked();
        }

        int HumanCount()
        {
            NetManager net = NetManager.Instance;
            if (net == null) return 0;
            if (net.IsHost && net.Host != null) return net.Host.PeerCount + 1;
            return net.CurrentMode == NetManager.Mode.Client ? 1 : 0;
        }

        void UpdatePlayerCountUI()
        {
            if (currentPlayerCountText == null) return;

            NetManager net = NetManager.Instance;
            if (net != null && net.IsHost)
            {
                currentPlayerCountText.text = HumanCount() + " / " + LanRoomConfig.HumanCount + "명"
                    + (LanRoomConfig.AiCount > 0 ? ("   AI " + LanRoomConfig.AiCount) : "");
            }
            else
            {
                // 참가자는 총원을 모른다 — 방장이 시작을 알릴 때까지 기다린다
                currentPlayerCountText.text = "접속됨";
            }
        }

        void PlayJoinPop()
        {
            if (currentPlayerCountText == null) return;
            currentPlayerCountText.rectTransform.localScale = Vector3.one * 1.25f;
            currentPlayerCountText.rectTransform
                .DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        IEnumerator AnimateDots(string label)
        {
            int n = 0;
            while (_matching && _countdown < 0f)
            {
                if (matchingStatusText != null)
                    matchingStatusText.text = label + new string('.', n);
                n = (n + 1) % 4;
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }

        int _shownHumans = -1;

        void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !_matching || _launching) return;

            // ★ 인원 표시는 이벤트에만 기대지 않는다.
            //
            //   OnPeerJoined는 '그 순간 구독하고 있던' NetManager에서만 온다.
            //   NetManager가 둘이 되거나 대표가 바뀌면 이벤트가 다른 쪽으로 흘러
            //   숫자가 영영 안 바뀐다(실제로 겪었다). 값 자체를 매 프레임 확인하면
            //   구독이 어긋나도 화면은 항상 맞다.
            int humans = HumanCount();
            if (humans != _shownHumans)
            {
                _shownHumans = humans;
                UpdatePlayerCountUI();
                PlayJoinPop();
            }

            // 참가자는 기다리기만 한다 — 시작 결정은 방장이 한다
            if (!net.IsHost) return;

            if (_countdown < 0f)
            {
                if (HumanCount() < LanRoomConfig.HumanCount) return;

                _countdown = countdownSeconds;
                PlayMatchingComplete();
                return;
            }

            _countdown -= Time.unscaledDeltaTime;

            int shown = Mathf.CeilToInt(Mathf.Max(0f, _countdown));
            if (countdownText != null && countdownText.text != shown.ToString())
                ShowCountdown(shown);

            if (_countdown > 0f) return;

            _countdown = -1f;
            HostLaunch();
        }

        /// <summary>인원이 찼을 때 — 상태 문구를 위로 올리고 카운트다운 자리를 만든다.</summary>
        void PlayMatchingComplete()
        {
            if (_dots != null) { StopCoroutine(_dots); _dots = null; }

            if (matchingStatusText != null)
            {
                matchingStatusText.text = "매칭 완료!";
                matchingStatusText.rectTransform
                    .DOAnchorPos(_matchingStatusOriginPos + new Vector2(0f, matchingCompleteSlideY), 0.4f)
                    .SetEase(Ease.OutCubic).SetUpdate(true);
            }

            if (cancelMatchingButton != null) cancelMatchingButton.SetActive(false);
            if (countdownText != null) countdownText.gameObject.SetActive(true);
        }

        void ShowCountdown(int number)
        {
            if (countdownText == null) return;

            countdownText.text = number.ToString();
            countdownText.rectTransform.localScale = Vector3.zero;
            countdownText.rectTransform
                .DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        void HostLaunch()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || _launching) return;

            _launching = true;

            if (countdownText != null) countdownText.gameObject.SetActive(false);
            if (gameStartText != null)
            {
                gameStartText.gameObject.SetActive(true);
                gameStartText.rectTransform.localScale = Vector3.zero;
                gameStartText.rectTransform
                    .DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            string scene = SceneFor(LanRoomConfig.Mode);

            _w.Begin(MsgType.LoadGameScene);
            _w.WriteByte((byte)LanRoomConfig.Mode);
            _w.WriteByte((byte)Mathf.Clamp(LanRoomConfig.AiCount, 0, 255));
            _w.WriteString(scene);
            _w.End();
            net.Host.Broadcast(_w);

            // ★ 방송이 실제로 나간 뒤에 씬을 넘긴다.
            //   같은 프레임에 LoadScene을 부르면 아직 보내지 않은 바이트가 남을 수 있다.
            //   "게임 시작!" 연출을 보여주는 시간이기도 하다.
            Invoke(nameof(LaunchNow), 0.6f);
        }

        void LaunchNow()
        {
            LoadGameScene(SceneFor(LanRoomConfig.Mode), LanRoomConfig.Mode, loadingScene);
        }

        string SceneFor(GameModeType m)
        {
            return m == GameModeType.Push ? gameScenePush : gameSceneAbsorb;
        }

        // ═════════════════════════════════════════════
        //  참가자: 지시받아 따라 들어간다
        // ═════════════════════════════════════════════
        void HandleClientMessage(MsgType type, NetReader r)
        {
            if (type != MsgType.LoadGameScene) return;

            GameModeType m = (GameModeType)r.ReadByte();
            int ai = r.ReadByte();
            string scene = r.ReadString();

            LanRoomConfig.Set(m, ai + 1, ai);   // 참가자는 표시용으로만 쓴다

            _launching = true;
            if (_dots != null) { StopCoroutine(_dots); _dots = null; }
            if (matchingStatusText != null) matchingStatusText.text = "게임 시작!";

            LoadGameScene(scene, m, loadingScene);
        }

        // ═════════════════════════════════════════════
        //  씬 로드
        // ═════════════════════════════════════════════

        /// <summary>
        /// 로비가 정한 모드. 게임 씬의 LanGameFlow가 이걸 보고 자기 설정을 맞춘다.
        ///
        /// ★ 왜 GameState.CurrentGameMode로는 부족한가
        ///   GameModeType은 { Absorb, Push } 두 값뿐이고 Absorb가 0이다.
        ///   즉 "아직 아무도 안 정했다"와 "흡수로 정했다"가 구분되지 않는다.
        ///   씬을 직접 열어 테스트할 때 로비 값이 없는 게 정상이므로,
        ///   그 둘을 구분해야 잘못된 경고를 띄우지 않는다.
        /// </summary>
        public static GameModeType? ChosenMode { get; private set; }

        public static void ClearChosenMode() { ChosenMode = null; }
        public static void SetChosenMode(GameModeType m) { ChosenMode = m; }

        /// <summary>씬 전환은 LanSceneFlow가 맡는다(커튼·소켓·상태 정리를 한 곳에서).</summary>
        public static void LoadGameScene(string sceneName, GameModeType m, string loadingSceneName = null)
        {
            LanSceneFlow.ToGame(sceneName, m);
        }

        public static string Label(GameModeType m)
        {
            return m == GameModeType.Push ? "밀치기" : "흡수";
        }
    }
}
