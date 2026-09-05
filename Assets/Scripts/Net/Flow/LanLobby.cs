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

        [Header("NickName UI")]
        [SerializeField] private RectTransform nicknamePanel;
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private GameObject nicknameWarningText;
        [SerializeField] private int nicknameMaxLength = 10;

        [Header("Select Room UI")]
        [SerializeField] private RectTransform roomChoicePanel;

        // 방 생성·방 참가 어느 쪽을 눌렀든 먼저 여기서 로컬인지 온라인인지 고른다.
        // 고른 뒤에야 원래 패널(방 설정 / 방 목록)이 뜬다.
        [Header("Local / Online 선택")]
        [SerializeField] private RectTransform netChoicePanel;

        [Header("Room Option UI")]
        [SerializeField] private RectTransform hostOptionPanel;
        [Tooltip("0 = absorb, 1 = push")]
        [SerializeField] private TMP_Dropdown modeDropdown;
        [SerializeField] private TMP_InputField portInput;

        // ★ 온라인 모드가 붙었을 때를 위한 자리 — 지금은 비워둬도 아무 일도 안 일어난다
        //   포트는 같은 랜에서 직접 붙을 때만 뜻이 있다. 온라인은 릴레이가 주소를 정하므로
        //   입력칸이 남아 있으면 "여기 뭘 넣어야 하나"가 된다.
        //   라벨까지 같이 숨겨야 하므로 입력칸 하나가 아니라 그 줄 전체를 받는다.
        //   씬에서 포트 라벨+입력칸을 감싸는 오브젝트를 여기 꽂으면 로컬 모드에서만 보인다.
        [Tooltip("포트 라벨과 입력칸을 감싸는 오브젝트. 로컬(LAN) 세션에서만 보인다. 비워두면 항상 보인다.")]
        [SerializeField] private GameObject localOnlyGroup;
        [SerializeField] private TMP_InputField totalPlayersInput;
        [SerializeField] private TMP_InputField aiCountInput;
        [SerializeField] private TMP_Text roomSettingWarningText;

        //주소를 직접 입력하지 않는다. 이 패널 안에서 LanRoomListUI가 같은 대역의 방 목록을 띄우고,
        //방을 고르면 LanRoomListUI.OnPick → JoinRoom(RoomHandle)로 들어온다
        [Header("Join Room UI")]
        [SerializeField] private RectTransform joinPanel;

        [Header("Matching UI")]
        [SerializeField] private RectTransform matchingPanel;
        [SerializeField] private TMP_Text matchingStatusText;
        [SerializeField] private TMP_Text currentPlayerCountText;
        [SerializeField] private TMP_Text roomAddressText;
        [SerializeField] private GameObject cancelMatchingButton;

        [Header("Countdown UI")]
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text gameStartText;

        [Header("애니메이션")]
        [SerializeField] Vector2 nicknameLeftPos = new Vector2(-400f, 0f);
        [SerializeField] float slideDuration = 0.45f;
        [SerializeField] Ease slideEase = Ease.OutCubic;
        [SerializeField] float popDelay = 0.2f;
        [SerializeField] float popDuration = 0.35f;
        [SerializeField] Ease popEase = Ease.OutBack;
        [SerializeField] float matchingCompleteSlideY = 60f;

        [Header("씬")]
        [SerializeField] private string gameSceneAbsorb = "Game_io_AbsorbMode";
        [SerializeField] private string gameScenePush = "Game_io_PushMode";

        [Header("기본값")]
        [SerializeField] private int defaultPort = NetConfig.DEFAULT_PORT;
        [SerializeField] private int defaultTotalPlayers = 4;
        [SerializeField] private int defaultAiCount = 2;
        [SerializeField] private float countdownSeconds = 3f;

        private Vector2 nicknameOriginPos;
        private Vector2 matchingStatusOriginPos;

        private float countdown = -1f;
        private bool launching;
        private bool matching;
        private Coroutine dots;
        private readonly NetWriter w = new NetWriter();

        private int shownHumans = -1;

        // ─────────────────────────────────────────────────────────
        //  대기 화면 상태 — 호스트가 방송하고 클라는 그대로 따른다
        // ─────────────────────────────────────────────────────────
        //
        // ★ 왜 필요한가
        //   인원수도 카운트다운도 호스트만 안다(HumanCount는 클라에서 언제나 1,
        //   Update의 카운트다운 블록도 !IsHost면 그 자리에서 빠져나간다).
        //   그래서 클라 화면은 "다른 참가자를 기다리는 중..." 에서 곧장
        //   "게임 시작!"으로 튀었다 — 매칭 완료도 3·2·1도 본 적이 없다.
        //   호스트가 바뀔 때마다 LobbyStatus를 쏴주면 양쪽이 같은 화면을 본다.
        private int netHumans = -1;   //-1 = 아직 못 받음
        private int netTotal;
        private int netAi;

        //같은 값을 60fps로 다시 쏘지 않기 위한 마지막 방송값
        private int sentHumans = -1;
        private int sentCountdown = -2;

        //"매칭 완료!" 연출은 한 번만. 호스트는 countdown이 -1→양수로 바뀌는 순간,
        //클라는 카운트다운이 실린 첫 LobbyStatus에서 재생한다
        private bool matchCompleteShown;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
            {
                Debug.LogError("[로비] NetManager가 없습니다. Main 씬에 NetManager를 올려주세요.");
                return;
            }

            net.RouteClient(MsgType.LoadGameScene, HandleLoadGameScene);
            net.RouteClient(MsgType.LobbyStatus, HandleLobbyStatus);
            net.OnPeerJoined += HandlePeerChanged;
            net.OnPeerLeft += HandlePeerChanged;
            net.OnDisconnected += HandleDisconnected;

            // ★ 끊김은 두 얼굴로 온다
            //   내가 끊은 것(OnDisconnected)과 상대가 사라진 것(OnConnectionLost)이
            //   다른 이벤트다. 대기 화면 입장에서는 둘 다 "이 방은 끝났다"로 같다.
            //   예전엔 앞의 것만 들어서, 방장이 취소하면 참가자 화면에 대기 패널이
            //   그대로 남아 영원히 사람을 기다렸다.
            net.OnConnectionLost += HandleDisconnected;

            //방 만들기·참가 실패는 세션이 알려준다. 예전엔 호출부가 반환값을 보고
            //net.LastError 를 직접 읽었는데, 온라인은 실패가 나중에 도착한다
            //세션이 아니라 NetManager 를 구독한다. 로컬/온라인을 갈아끼워도 끊기지 않는다
            net.OnSessionFailed += ShowLobbyError;
            net.OnRoomReady += HandleRoomReady;

            LanScoreboard.Clear();
            LanRoomConfig.Clear();

            CacheOriginPositions();
            ResetPanels();
            FillDefaults();
        }

        private void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !matching || launching)
                return;

            //클라의 인원 표시는 LobbyStatus가 갱신한다. 여기서 폴링하면
            //자기 자신만 세는 HumanCount가 다시 덮어써 "1 / 4명"에서 멈춘다
            if (!net.IsHost)
                return;

            int humans = HumanCount();
            if (humans != shownHumans)
            {
                shownHumans = humans;
                UpdatePlayerCountUI();
                PlayJoinPop();
            }

            if (countdown < 0f)
            {
                if (humans < LanRoomConfig.HumanCount)
                {
                    HostBroadcastStatus();
                    return;
                }

                countdown = countdownSeconds;
                PlayMatchingComplete();
                HostBroadcastStatus();
                return;
            }

            countdown -= Time.unscaledDeltaTime;

            ShowCountdown(Mathf.CeilToInt(Mathf.Max(0f, countdown)));
            HostBroadcastStatus();

            if (countdown > 0f)
                return;

            countdown = -1f;
            HostLaunch();
        }

        //대기 화면에서 카운트다운이 아직 안 돌고 있음을 뜻하는 값.
        //바이트 하나로 보내려고 -1 대신 255를 쓴다
        private const int CD_WAITING = 255;

        /// <summary>
        /// 호스트만 아는 대기 화면 상태(인원·정원·AI·카운트다운)를 클라에 알린다.
        /// 값이 바뀔 때만 나가므로 프레임마다 불러도 된다.
        /// </summary>
        private void HostBroadcastStatus(bool force = false)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;

            int humans = HumanCount();
            int shown = countdown < 0f ? CD_WAITING : Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0f, countdown)), 0, 254);

            if (!force && humans == sentHumans && shown == sentCountdown)
                return;

            sentHumans = humans;
            sentCountdown = shown;

            w.Begin(MsgType.LobbyStatus);
            w.WriteByte((byte)Mathf.Clamp(humans, 0, 255));
            w.WriteByte((byte)Mathf.Clamp(LanRoomConfig.HumanCount, 0, 255));
            w.WriteByte((byte)Mathf.Clamp(LanRoomConfig.AiCount, 0, 255));
            w.WriteByte((byte)shown);
            w.End();
            net.Broadcast(w);
        }

        /// <summary>호스트가 보낸 대기 화면 상태를 그대로 재생한다(클라 전용).</summary>
        private void HandleLobbyStatus(NetReader r)
        {
            int humans = r.ReadByte();
            int total = r.ReadByte();
            int ai = r.ReadByte();
            int cd = r.ReadByte();

            if (!matching || launching)
                return;

            bool changed = humans != netHumans;

            netHumans = humans;
            netTotal = total;
            netAi = ai;

            UpdatePlayerCountUI();
            if (changed)
                PlayJoinPop();

            if (cd == CD_WAITING)
                return;

            if (!matchCompleteShown)
                PlayMatchingComplete();

            ShowCountdown(cd);
        }

        private void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net != null)
            {
                net.UnrouteClient(MsgType.LoadGameScene);
                net.UnrouteClient(MsgType.LobbyStatus);
                net.OnPeerJoined -= HandlePeerChanged;
                net.OnPeerLeft -= HandlePeerChanged;

                net.OnSessionFailed -= ShowLobbyError;
                net.OnRoomReady -= HandleRoomReady;
                net.OnDisconnected -= HandleDisconnected;
                net.OnConnectionLost -= HandleDisconnected;
            }
            //트윈은 DOTween 엔진이 들고 있어 이 컴포넌트보다 오래 산다.
            //게임 시작 연출(0.4초)이 끝나기 전에 씬이 바뀌므로, 정리하지 않으면
            //파괴된 RectTransform을 건드린다. 이벤트 구독 해제와 같은 이유다
            KillTweens();

            if (Instance == this)
                Instance = null;
        }

        private void KillTweens()
        {
            //RectTransform은 UnityEngine.Object라 ?. 를 쓰면 안 된다.
            //?. 는 C# 참조만 보므로 이미 파괴된(페이크 널) 오브젝트를 통과시킨다
            if (nicknamePanel != null)
                nicknamePanel.DOKill();
            if (roomChoicePanel != null)
                roomChoicePanel.DOKill();
            if (hostOptionPanel != null)
                hostOptionPanel.DOKill();
            if (joinPanel != null)
                joinPanel.DOKill();
            if (matchingPanel != null)
                matchingPanel.DOKill();

            if (matchingStatusText != null)
                matchingStatusText.rectTransform.DOKill();
            if (currentPlayerCountText != null)
                currentPlayerCountText.rectTransform.DOKill();
            if (countdownText != null)
                countdownText.rectTransform.DOKill();
            if (gameStartText != null)
                gameStartText.rectTransform.DOKill();
        }

        private void CacheOriginPositions()
        {
            if (nicknamePanel != null)
                nicknameOriginPos = nicknamePanel.anchoredPosition;
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
            if (roomSettingWarningText != null)
                roomSettingWarningText.gameObject.SetActive(false);
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);
            if (gameStartText != null)
                gameStartText.gameObject.SetActive(false);
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

        private void Pop(RectTransform rt, float delay = 0f)
        {
            if (rt == null)
                return;

            //진행 중이던 트윈을 먼저 없앤다. 특히 Unpop이 예약해둔 OnComplete(SetActive(false))가
            //남아 있으면, 방금 켠 패널을 0.2초 뒤에 꺼버려 화면이 빈 채로 멈춘다.
            //(방 만들기 → 뒤로 를 빠르게 누르면 재현됐다)
            rt.DOKill();

            rt.gameObject.SetActive(true);
            rt.localScale = Vector3.zero;

            rt.DOScale(Vector3.one, popDuration)
              .SetEase(popEase).SetDelay(delay).SetUpdate(true);
        }

        private void Unpop(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeSelf)
                return;

            rt.DOKill();

            rt.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack).SetUpdate(true)
              .OnComplete(() => rt.gameObject.SetActive(false));
        }

        private void SlideNicknameOut()
        {
            if (nicknamePanel == null)
                return;
            nicknamePanel.DOKill();
            nicknamePanel.DOAnchorPos(nicknameLeftPos, slideDuration)
                         .SetEase(slideEase).SetUpdate(true);
        }

        private void SlideNicknameBack()
        {
            if (nicknamePanel == null)
                return;
            nicknamePanel.DOKill();
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
                return;
            }

            if (nicknameWarningText != null)
                nicknameWarningText.SetActive(false);
            LanRoomConfig.Nickname = nick;

            SlideNicknameOut();

            Pop(roomChoicePanel, popDelay);
        }

        // ★ 어디로 갈지는 여기서 정해두고, 로컬/온라인을 고른 뒤에 연다
        //   버튼 두 개(로컬·온라인)가 방 생성과 방 참가 양쪽에서 재사용되므로,
        //   "고르고 나면 어디로 가야 하는지"를 패널이 아니라 여기서 기억한다.
        private enum AfterChoice { None, Host, Join }

        private AfterChoice afterChoice;

        public void OnClickCreateRoom()
        {
            OpenNetChoice(AfterChoice.Host);
        }

        public void OnClickJoinRoom()
        {
            OpenNetChoice(AfterChoice.Join);
        }

        private void OpenNetChoice(AfterChoice next)
        {
            afterChoice = next;

            if (netChoicePanel == null)
            {
                //꽂는 것을 잊으면 조용히 로컬로 가는 대신 말한다. 조용히 넘어가면
                //"온라인을 고른 적이 없는데 왜 랜이지"를 나중에 화면에서 찾게 된다
                Debug.LogError("[로비] netChoicePanel 이 비어 있습니다. 로컬로 진행합니다.");
                ChooseNet(false);
                return;
            }

            Pop(netChoicePanel, popDelay);
        }

        /// <summary>선택 패널의 '로컬(같은 와이파이)' 버튼.</summary>
        public void OnClickLocal()
        {
            ChooseNet(false);
        }

        /// <summary>선택 패널의 '온라인' 버튼.</summary>
        public void OnClickOnline()
        {
            ChooseNet(true);
        }

        private void ChooseNet(bool online)
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return;

            net.UseOnline(online);

            //UseOnline 은 거절할 수 있다(온라인 코드가 빠진 빌드, 접속 중 등).
            //그때 다음 화면으로 넘어가면 고른 것과 다른 전송으로 방을 만들게 된다
            if (net.IsOnline != online)
                return;

            Unpop(netChoicePanel);

            if (afterChoice == AfterChoice.Host)
            {
                //포트 입력칸은 로컬에서만 뜻이 있다. 세션이 정해진 지금에야 판단할 수 있다
                ApplyLocalOnlyVisibility();
                Pop(hostOptionPanel, popDelay);
            }
            else
            {
                Pop(joinPanel, popDelay);
            }

            afterChoice = AfterChoice.None;
        }

        //세션이 로컬인지에 따라 로컬 전용 입력을 보이고 숨긴다.
        //꽂아둔 게 없으면 아무것도 하지 않는다 — 지금(LAN 전용)은 그래도 맞다
        private void ApplyLocalOnlyVisibility()
        {
            if (localOnlyGroup == null)
                return;

            NetManager net = NetManager.Instance;
            bool local = net == null || net.Session == null || net.Session.IsLocal;

            localOnlyGroup.SetActive(local);
        }

        public void OnClickGenerate()
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
                if (roomSettingWarningText != null)
                {
                    roomSettingWarningText.gameObject.SetActive(true);
                    roomSettingWarningText.text = "플레이어가 최소 한 명 이상이어야 합니다!";
                }
                return;
            }
            if (ai >= total)
            {
                if (roomSettingWarningText != null)
                {
                    roomSettingWarningText.gameObject.SetActive(true);
                    roomSettingWarningText.text = "사람이 최소 한 명 이상이어야 합니다!";
                }
                return;
            }

            LanRoomConfig.Set(mode, total, ai);

            BeginConnecting();

            //실패 사유는 세션이 OnFailed 로 알린다(ShowLobbyError 가 받는다)
            RoomOptions opts = new RoomOptions
            {
                RoomName = LanRoomConfig.Nickname,
                LocalPort = port
            };

            if (net.Session == null || !net.Session.CreateRoom(opts))
                return;

            //방을 만든 사람에게는 자기 닉네임이 곧 방 이름이다
            if (roomAddressText != null)
                roomAddressText.text = LanRoomConfig.Nickname;

            Unpop(hostOptionPanel);
            OpenMatching(roomReady ? MATCHING_LABEL : CONNECTING_LABEL);
        }

        /// <summary>목록에서 고른 방에 붙는다. 실패 사유는 세션이 OnFailed 로 알린다.</summary>
        public void JoinRoom(RoomHandle room)
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.Session == null || room == null)
                return;

            BeginConnecting();

            if (!net.Session.JoinRoom(room))
                return;

            // ★ 주소가 아니라 방 이름을 보여준다
            //   예전엔 로컬에서 "192.168.0.5 : 7777" 이 떴다. 온라인에는 보여줄
            //   주소가 없어 방 이름이 떴는데, 같은 자리에 두 가지 다른 것이 뜨면
            //   화면이 무엇을 말하는 칸인지 흐려진다. 사람이 알아야 하는 건
            //   "누구 방에 있는가"이지 IP 가 아니다.
            if (roomAddressText != null)
                roomAddressText.text = room.HostName;

            Unpop(joinPanel);
            OpenMatching(roomReady ? MATCHING_LABEL : CONNECTING_LABEL);
        }


        //호스트와 클라가 서로 다른 문구를 보면 같은 방에 있다는 느낌이 안 든다.
        //양쪽 다 "판이 차기를 기다린다"는 같은 상태이므로 문구도 하나로 둔다
        private const string MATCHING_LABEL = "다른 참가자를 기다리는 중";

        // ★ 요청을 보낸 것과 방에 들어간 것은 다르다
        //   LAN 은 소켓을 여는 데까지가 동기라 이 문구가 한순간 스치고 지나가지만,
        //   클라는 그때도 아직 자기 번호를 못 받았다(호스트의 환영 인사가 와야 한다).
        //   릴레이는 방 만들기 성공 자체가 몇백 ms 뒤에 온다. 그동안 "다른 참가자를
        //   기다리는 중"을 띄우면, 실패했을 때 아무도 오지 않는 방을 기다린 셈이 된다.
        private const string CONNECTING_LABEL = "연결 중";

        // ★ 신호가 화면보다 먼저 올 수 있다
        //   LAN 호스트는 CreateRoom 이 성공하는 그 자리에서 OnRoomReady 가 터진다.
        //   대기 화면을 여는 건 그다음 줄이라, 그때 matching 은 아직 false 다.
        //   '왔는가'를 기억해 두지 않고 그 순간에만 반응하면 호스트는 영원히
        //   "연결 중..." 에 갇힌다. 어느 쪽이 먼저 와도 되게 상태로 들고 있는다.
        private bool roomReady;

        //방을 만들거나 참가하기 직전에 부른다
        private void BeginConnecting()
        {
            roomReady = false;
        }

        /// <summary>방에 실제로 들어갔다. 그제서야 '기다린다'는 말이 사실이 된다.</summary>
        private void HandleRoomReady()
        {
            roomReady = true;

            if (!matching)
                return;

            SetMatchingLabel(MATCHING_LABEL);

            //이제서야 인원을 말할 수 있다. 위 문구와 아래 숫자가 같이 바뀐다
            UpdatePlayerCountUI();
        }

        private void SetMatchingLabel(string label)
        {
            if (matchingStatusText != null)
                matchingStatusText.text = label;

            if (dots != null)
                StopCoroutine(dots);
            dots = StartCoroutine(AnimateDots(label));
        }

        //실패했으면 대기 화면으로 넘어가지 않는다. 넘어가면 영원히 기다리는 것처럼 보인다
        private void ShowLobbyError(string message)
        {
            Debug.LogWarning("[로비] " + message);

            if (nicknameWarningText == null)
                return;

            TMP_Text label = nicknameWarningText.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = message;

            nicknameWarningText.SetActive(true);
        }

        private void OpenMatching(string label)
        {
            matching = true;
            matchCompleteShown = false;
            shownHumans = -1;
            netHumans = -1;
            sentHumans = -1;
            sentCountdown = -2;

            if (cancelMatchingButton != null)
                cancelMatchingButton.SetActive(true);
            if (matchingStatusText != null)
            {
                matchingStatusText.rectTransform.anchoredPosition = matchingStatusOriginPos;
                matchingStatusText.text = label;
            }

            Pop(matchingPanel, popDelay);

            if (dots != null)
                StopCoroutine(dots);
            dots = StartCoroutine(AnimateDots(label));

            UpdatePlayerCountUI();
        }

        public void OnCancelMatchingClicked()
        {
            NetManager net = NetManager.Instance;
            if (!NetManager.Offline)
                net.Shutdown();

            matching = false;
            countdown = -1f;
            launching = false;

            CancelInvoke(nameof(LaunchNow));

            pendingScene = null;
            matchCompleteShown = false;
            shownHumans = -1;
            netHumans = -1;
            sentHumans = -1;
            sentCountdown = -2;

            //방 알리기는 위의 Shutdown 이 끊기면서 세션이 알아서 멈춘다. 여기선 찾기만 끈다
            if (net != null && net.Session != null)
                net.Session.StopBrowsing();

            if (dots != null)
            {
                StopCoroutine(dots);
                dots = null;
            }
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            Unpop(matchingPanel);
        }

        //방 설정·방 목록·로컬/온라인 선택이 같은 뒤로 버튼을 쓴다.
        //Unpop 은 꺼져 있는 패널에는 아무 일도 하지 않으므로 전부 불러도 된다
        public void OnClickBack()
        {
            Unpop(hostOptionPanel);
            Unpop(joinPanel);
            Unpop(netChoicePanel);
        }

        public void OnClickBackToNickname()
        {
            Unpop(roomChoicePanel);
            SlideNicknameBack();
        }

        private static int ParseInt(TMP_InputField f, int fallback)
        {
            int v;
            return (f != null && int.TryParse(f.text, out v)) ? v : fallback;
        }


        private void HandlePeerChanged(int peerId)
        {
            UpdatePlayerCountUI();
            PlayJoinPop();

            //새로 들어온 사람에게도 지금 인원이 몇인지 알려야 한다.
            //값이 안 바뀌었어도(예: 나갔다가 같은 수로 다시 참) 강제로 한 번 보낸다
            HostBroadcastStatus(true);
        }

        private void HandleDisconnected()
        {
            if (launching)
                return;
            OnCancelMatchingClicked();
        }

        private int HumanCount()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return 0;
            if (net.IsHost)
                return net.PeerCount + 1;

            //클라는 다른 클라가 몇 명인지 모른다. 호스트만 아는 값이라 자기 자신만 센다
            return net.CurrentMode == NetManager.Mode.Client ? 1 : 0;
        }

        private void UpdatePlayerCountUI()
        {
            if (currentPlayerCountText == null)
                return;

            // ★ 방에 들어가기 전에는 인원을 말하지 않는다
            //   예전엔 여기서 곧바로 "접속됨"이 떴다. LAN 은 방 만들기가 동기라
            //   그 글자가 스치고 지나갔지만, 릴레이는 몇백 ms 를 기다리므로
            //   <b>아직 방이 없는데 접속됐다고 말하는</b> 화면이 그대로 보인다.
            //   위쪽 문구가 "연결 중..." 인 동안 아래는 비어 있어야 앞뒤가 맞는다.
            if (!roomReady)
            {
                currentPlayerCountText.text = "";
                return;
            }

            NetManager net = NetManager.Instance;

            int humans, total, ai;

            if (net != null && net.IsHost)
            {
                humans = HumanCount();
                total = LanRoomConfig.HumanCount;
                ai = LanRoomConfig.AiCount;
            }
            else
            {
                //호스트가 보내주기 전까지는 숫자를 지어내지 않는다
                if (netHumans < 0)
                {
                    currentPlayerCountText.text = "접속됨";
                    return;
                }

                humans = netHumans;
                total = netTotal;
                ai = netAi;
            }

            currentPlayerCountText.text = humans + " / " + total + "명"
                + (ai > 0 ? ("   AI " + ai) : "");
        }

        private void PlayJoinPop()
        {
            if (currentPlayerCountText == null)
                return;
            currentPlayerCountText.rectTransform.DOKill();
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

        private void PlayMatchingComplete()
        {
            matchCompleteShown = true;

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

            //0은 띄우지 않는다 — 마지막 한 프레임만 보였다가 곧바로 시작 연출에 가려진다
            if (number < 1)
                return;

            //호출부(호스트 Update / 클라 수신)가 매 프레임 불러도 숫자가 바뀔 때만 튄다
            if (countdownText.text == number.ToString())
                return;

            countdownText.rectTransform.DOKill();
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

            //판이 시작됐으니 목록에서 내린다. 연결은 그대로 유지해야 한다
            if (net.Session != null)
                net.Session.StopAdvertising();

            string scene = SceneFor(LanRoomConfig.Mode);

            //총 인원까지 보낸다. 예전엔 봇 수만 보내고 클라가 Set(m, ai + 1, ai)로 지어냈는데,
            //그건 LanRoomConfig.Set의 클램프(AiCount <= TotalPlayers - 1)를 통과시키려는 꼼수였고
            //그 결과 클라의 HumanCount가 언제나 1이 됐다
            w.Begin(MsgType.LoadGameScene);
            w.WriteByte((byte)LanRoomConfig.Mode);
            w.WriteByte((byte)Mathf.Clamp(LanRoomConfig.AiCount, 0, 255));
            w.WriteByte((byte)Mathf.Clamp(LanRoomConfig.TotalPlayers, 1, 255));
            w.WriteString(scene);
            w.End();
            net.Broadcast(w);

            BeginLaunch(scene, LanRoomConfig.Mode);
        }

        //"게임 시작!"을 보여주는 시간. 호스트와 클라가 같은 값을 써야
        //양쪽이 같은 순간에 로딩 커튼으로 넘어간다.
        //예전엔 호스트만 0.6초를 기다리고 클라는 수신 즉시 씬을 로드해서,
        //클라가 0.6초 먼저 게임 씬에 들어가 인게임 카운트다운까지 어긋났다
        private const float LAUNCH_DELAY = 0.6f;

        private string pendingScene;
        private GameModeType pendingMode;

        /// <summary>양쪽 공용 — "게임 시작!" 연출을 띄우고 잠시 뒤 씬을 넘긴다.</summary>
        private void BeginLaunch(string scene, GameModeType mode)
        {
            if (launching && pendingScene != null)
                return;

            launching = true;
            pendingScene = scene;
            pendingMode = mode;

            if (dots != null)
            {
                StopCoroutine(dots);
                dots = null;
            }

            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            if (gameStartText != null)
            {
                gameStartText.gameObject.SetActive(true);
                gameStartText.rectTransform.localScale = Vector3.zero;
                gameStartText.rectTransform
                    .DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
            }

            Invoke(nameof(LaunchNow), LAUNCH_DELAY);
        }

        private void LaunchNow()
        {
            LanSceneFlow.ToGame(pendingScene, pendingMode);
        }

        private string SceneFor(GameModeType m)
        {
            return m == GameModeType.Push ? gameScenePush : gameSceneAbsorb;
        }

        private void HandleLoadGameScene(NetReader r)
        {
            GameModeType m = (GameModeType)r.ReadByte();
            int ai = r.ReadByte();
            int total = r.ReadByte();
            string scene = r.ReadString();

            LanRoomConfig.Set(m, total, ai);

            //호스트가 카운트다운을 다 못 보여준 채(패킷 유실·늦은 접속) 여기로 왔다면
            //적어도 "매칭 완료!" 상태는 맞춰두고 시작 연출로 넘어간다
            if (!matchCompleteShown)
                PlayMatchingComplete();

            //호스트와 같은 연출·같은 대기시간으로 넘어간다.
            //예전엔 여기서 상태 문구만 "게임 시작!"으로 바꾸고 곧바로 씬을 로드해,
            //클라 화면에서는 매칭 완료도 3·2·1도 없이 갑자기 화면이 넘어갔다
            BeginLaunch(scene, m);
        }

        public static string Label(GameModeType m)
        {
            return m == GameModeType.Push ? "밀치기" : "흡수";
        }
    }
}
