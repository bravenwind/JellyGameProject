using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using DG.Tweening;
using TMPro;

namespace JellyNet
{
    public class LanLobby : MonoBehaviour
    {
        public static LanLobby Instance { get; private set; }

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
        public TMP_Text matchingStatusText;
        public TMP_Text currentPlayerCountText;
        public TMP_Text roomAddressText;
        public GameObject cancelMatchingButton;

        [Header("카운트다운")]
        public TMP_Text countdownText;
        public TMP_Text gameStartText;

        [Header("공통")]
        public TMP_Text statusText;

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
        public int defaultPort = NetConfig.DEFAULT_PORT;
        public int defaultTotalPlayers = 4;
        public int defaultAiCount = 2;
        public float countdownSeconds = 3f;

        private Vector2 nicknameOriginPos;
        private Vector2 roomChoiceOriginPos;
        private Vector2 matchingStatusOriginPos;

        private float countdown = -1f;
        private bool launching;
        private bool matching;
        private Coroutine dots;
        private readonly NetWriter w = new NetWriter();

        private void Awake() { Instance = this; }

        private void Start()
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

            LanScoreboard.Clear();
            LanRoomConfig.Clear();
            ClearChosenMode();

            CacheOriginPositions();
            ResetPanels();
            FillDefaults();
        }

        private void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net != null)
            {
                net.OnClientMessage -= HandleClientMessage;
                net.OnPeerJoined -= HandlePeerChanged;
                net.OnPeerLeft -= HandlePeerChanged;
                net.OnDisconnected -= HandleDisconnected;
            }
            if (Instance == this)
                Instance = null;
        }

        private void CacheOriginPositions()
        {
            if (nicknamePanel != null)
                nicknameOriginPos = nicknamePanel.anchoredPosition;
            if (roomChoicePanel != null)
                roomChoiceOriginPos = roomChoicePanel.anchoredPosition;
            if (matchingStatusText != null)
                matchingStatusOriginPos = matchingStatusText.rectTransform.anchoredPosition;
        }

        private void ResetPanels()
        {
            Fold(roomChoicePanel);
            Fold(hostOptionPanel);
            Fold(joinPanel);
            Fold(matchingPanel);

            if (nicknamePanel != null)
            {
                nicknamePanel.anchoredPosition = nicknameOriginPos;
                nicknamePanel.gameObject.SetActive(false);
            }

            if (nicknameWarningText != null)
                nicknameWarningText.SetActive(false);
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);
            if (gameStartText != null)
                gameStartText.gameObject.SetActive(false);
            if (joinHintText != null)
                joinHintText.text = "ip:port 형식으로 입력  (예: 192.168.0.5:7777)";
        }

        private static void Fold(RectTransform rt)
        {
            if (rt == null)
                return;
            rt.localScale = Vector3.zero;
            rt.gameObject.SetActive(false);
        }

        private void FillDefaults()
        {
            if (portInput != null && string.IsNullOrEmpty(portInput.text))
                portInput.text = defaultPort.ToString();
            if (totalPlayersInput != null && string.IsNullOrEmpty(totalPlayersInput.text))
                totalPlayersInput.text = defaultTotalPlayers.ToString();
            if (aiCountInput != null && string.IsNullOrEmpty(aiCountInput.text))
                aiCountInput.text = defaultAiCount.ToString();
            if (nicknameInput != null && string.IsNullOrEmpty(nicknameInput.text))
            {
                nicknameInput.text = !string.IsNullOrEmpty(LanRoomConfig.Nickname)
                    ? LanRoomConfig.Nickname
                    : ("플레이어" + Random.Range(100, 1000));
            }
        }

        private void Status(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void Pop(RectTransform rt, Vector2? at = null, float delay = 0f)
        {
            if (rt == null)
                return;

            rt.gameObject.SetActive(true);
            rt.localScale = Vector3.zero;
            if (at.HasValue)
                rt.anchoredPosition = at.Value;

            rt.DOScale(Vector3.one, popDuration)
              .SetEase(popEase).SetDelay(delay).SetUpdate(true);
        }

        private void Unpop(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeSelf)
                return;

            rt.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack).SetUpdate(true)
              .OnComplete(() => rt.gameObject.SetActive(false));
        }

        private void SlideNicknameOut()
        {
            if (nicknamePanel == null)
                return;
            nicknamePanel.DOAnchorPos(nicknameLeftPos, slideDuration)
                         .SetEase(slideEase).SetUpdate(true);
        }

        private void SlideNicknameBack()
        {
            if (nicknamePanel == null)
                return;
            nicknamePanel.gameObject.SetActive(true);
            nicknamePanel.DOAnchorPos(nicknameOriginPos, slideDuration)
                         .SetEase(slideEase).SetUpdate(true);
        }

        public void OnClickNicknameConfirm()
        {
            string nick = nicknameInput != null ? nicknameInput.text.Trim() : "";

            if (string.IsNullOrEmpty(nick) || nick.Length > nicknameMaxLength)
            {
                if (nicknameWarningText != null)
                    nicknameWarningText.SetActive(true);
                Status("닉네임은 1~" + nicknameMaxLength + "자로 입력해주세요.");
                return;
            }

            if (nicknameWarningText != null)
                nicknameWarningText.SetActive(false);
            LanRoomConfig.Nickname = nick;
            Status("");

            SlideNicknameOut();

            Pop(roomChoicePanel,
                overrideRoomChoicePos ? (Vector2?)roomChoiceRightPos : roomChoiceOriginPos,
                popDelay);
        }

        public void OnClickCreateRoom()
        {
            Unpop(roomChoicePanel);
            Pop(hostOptionPanel, null, popDelay);
            Status("");
        }

        public void OnClickJoinRoom()
        {
            Unpop(roomChoicePanel);
            Pop(joinPanel, null, popDelay);
            Status("");
        }

        public void OnClickStartMatching()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return;

            int port = ParseInt(portInput, defaultPort);
            int total = ParseInt(totalPlayersInput, defaultTotalPlayers);
            int ai = ParseInt(aiCountInput, defaultAiCount);
            GameModeType mode = (modeDropdown != null && modeDropdown.value == 1)
                ? GameModeType.Push : GameModeType.Absorb;

            if (total < 1)
            {
                Status("총 플레이어 수는 1명 이상이어야 합니다.");
                return;
            }
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

            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StartBeacon();

            Unpop(hostOptionPanel);
            OpenMatching("참가자를 기다리는 중");
        }

        public void OnClickConnect()
        {
            string raw = joinAddressInput != null ? joinAddressInput.text.Trim() : "";
            if (string.IsNullOrEmpty(raw))
            {
                Status("주소를 입력해주세요.");
                return;
            }

            string ip; int port;
            if (!TryParseAddress(raw, out ip, out port))
            {
                Status("형식이 올바르지 않습니다. ip:port 로 입력해주세요. (예: 192.168.0.5:7777)");
                return;
            }

            JoinRoom(ip, port);
        }

        public void JoinRoom(string ip, int port)
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return;

            net.joinIp = ip;
            net.port = port;
            net.JoinHost();

            if (roomAddressText != null)
                roomAddressText.text = ip + " : " + port;

            Unpop(joinPanel);
            OpenMatching("방장을 기다리는 중");
        }

        private void OpenMatching(string label)
        {
            matching = true;
            Status("");

            if (cancelMatchingButton != null)
                cancelMatchingButton.SetActive(true);
            if (matchingStatusText != null)
            {
                matchingStatusText.rectTransform.anchoredPosition = matchingStatusOriginPos;
                matchingStatusText.text = label;
            }

            Pop(matchingPanel, null, popDelay);

            if (dots != null)
                StopCoroutine(dots);
            dots = StartCoroutine(AnimateDots(label));

            UpdatePlayerCountUI();
        }

        public void OnCancelMatchingClicked()
        {
            NetManager net = NetManager.Instance;
            if (net != null && net.CurrentMode != NetManager.Mode.None)
                net.Shutdown();

            matching = false;
            countdown = -1f;
            launching = false;

            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StopAll();

            if (dots != null)
            {
                StopCoroutine(dots);
                dots = null;
            }
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            Unpop(matchingPanel);
            Pop(roomChoicePanel, null, popDelay);
            Status("");
        }

        public void OnClickBack()
        {
            Unpop(hostOptionPanel);
            Unpop(joinPanel);
            Pop(roomChoicePanel, null, popDelay);
            Status("");
        }

        public void OnClickBackToNickname()
        {
            Unpop(roomChoicePanel);
            SlideNicknameBack();
            Status("");
        }

        public static bool TryParseAddress(string raw, out string ip, out int port)
        {
            ip = raw;
            port = NetConfig.DEFAULT_PORT;

            int colon = raw.LastIndexOf(':');
            if (colon < 0)
                return raw.Length > 0;

            ip = raw.Substring(0, colon).Trim();
            string portPart = raw.Substring(colon + 1).Trim();

            if (ip.Length == 0)
                return false;
            if (!int.TryParse(portPart, out port))
                return false;
            return port > 0 && port <= 65535;
        }

        private static int ParseInt(TMP_InputField f, int fallback)
        {
            int v;
            return (f != null && int.TryParse(f.text, out v)) ? v : fallback;
        }

        private static string LocalAddress()
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

        private void HandlePeerChanged(NetHost.Peer peer)
        {
            UpdatePlayerCountUI();
            PlayJoinPop();
        }

        private void HandleDisconnected()
        {
            if (launching)
                return;
            Status("연결이 끊어졌습니다.");
            OnCancelMatchingClicked();
        }

        private int HumanCount()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return 0;
            if (net.IsHost && net.Host != null)
                return net.Host.PeerCount + 1;
            return net.CurrentMode == NetManager.Mode.Client ? 1 : 0;
        }

        private void UpdatePlayerCountUI()
        {
            if (currentPlayerCountText == null)
                return;

            NetManager net = NetManager.Instance;
            if (net != null && net.IsHost)
            {
                currentPlayerCountText.text = HumanCount() + " / " + LanRoomConfig.HumanCount + "명"
                    + (LanRoomConfig.AiCount > 0 ? ("   AI " + LanRoomConfig.AiCount) : "");
            }
            else
            {
                currentPlayerCountText.text = "접속됨";
            }
        }

        private void PlayJoinPop()
        {
            if (currentPlayerCountText == null)
                return;
            currentPlayerCountText.rectTransform.localScale = Vector3.one * 1.25f;
            currentPlayerCountText.rectTransform
                .DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        private IEnumerator AnimateDots(string label)
        {
            int n = 0;
            while (matching && countdown < 0f)
            {
                if (matchingStatusText != null)
                    matchingStatusText.text = label + new string('.', n);
                n = (n + 1) % 4;
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }

        private int shownHumans = -1;

        private void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !matching || launching)
                return;

            int humans = HumanCount();
            if (humans != shownHumans)
            {
                shownHumans = humans;
                UpdatePlayerCountUI();
                PlayJoinPop();
            }

            if (!net.IsHost)
                return;

            if (countdown < 0f)
            {
                if (HumanCount() < LanRoomConfig.HumanCount)
                    return;

                countdown = countdownSeconds;
                PlayMatchingComplete();
                return;
            }

            countdown -= Time.unscaledDeltaTime;

            int shown = Mathf.CeilToInt(Mathf.Max(0f, countdown));
            if (countdownText != null && countdownText.text != shown.ToString())
                ShowCountdown(shown);

            if (countdown > 0f)
                return;

            countdown = -1f;
            HostLaunch();
        }

        private void PlayMatchingComplete()
        {
            if (dots != null)
            {
                StopCoroutine(dots);
                dots = null;
            }

            if (matchingStatusText != null)
            {
                matchingStatusText.text = "매칭 완료!";
                matchingStatusText.rectTransform
                    .DOAnchorPos(matchingStatusOriginPos + new Vector2(0f, matchingCompleteSlideY), 0.4f)
                    .SetEase(Ease.OutCubic).SetUpdate(true);
            }

            if (cancelMatchingButton != null)
                cancelMatchingButton.SetActive(false);
            if (countdownText != null)
                countdownText.gameObject.SetActive(true);
        }

        private void ShowCountdown(int number)
        {
            if (countdownText == null)
                return;

            countdownText.text = number.ToString();
            countdownText.rectTransform.localScale = Vector3.zero;
            countdownText.rectTransform
                .DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        private void HostLaunch()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || launching)
                return;

            launching = true;

            if (LanDiscovery.Instance != null)
                LanDiscovery.Instance.StopAll();

            if (countdownText != null)
                countdownText.gameObject.SetActive(false);
            if (gameStartText != null)
            {
                gameStartText.gameObject.SetActive(true);
                gameStartText.rectTransform.localScale = Vector3.zero;
                gameStartText.rectTransform
                    .DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            string scene = SceneFor(LanRoomConfig.Mode);

            w.Begin(MsgType.LoadGameScene);
            w.WriteByte((byte)LanRoomConfig.Mode);
            w.WriteByte((byte)Mathf.Clamp(LanRoomConfig.AiCount, 0, 255));
            w.WriteString(scene);
            w.End();
            net.Host.Broadcast(w);

            Invoke(nameof(LaunchNow), 0.6f);
        }

        private void LaunchNow()
        {
            LoadGameScene(SceneFor(LanRoomConfig.Mode), LanRoomConfig.Mode, loadingScene);
        }

        private string SceneFor(GameModeType m)
        {
            return m == GameModeType.Push ? gameScenePush : gameSceneAbsorb;
        }

        private void HandleClientMessage(MsgType type, NetReader r)
        {
            if (type != MsgType.LoadGameScene)
                return;

            GameModeType m = (GameModeType)r.ReadByte();
            int ai = r.ReadByte();
            string scene = r.ReadString();

            LanRoomConfig.Set(m, ai + 1, ai);

            launching = true;
            if (dots != null)
            {
                StopCoroutine(dots);
                dots = null;
            }
            if (matchingStatusText != null)
                matchingStatusText.text = "게임 시작!";

            LoadGameScene(scene, m, loadingScene);
        }

        public static GameModeType? ChosenMode { get; private set; }

        public static void ClearChosenMode() { ChosenMode = null; }
        public static void SetChosenMode(GameModeType m) { ChosenMode = m; }

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
