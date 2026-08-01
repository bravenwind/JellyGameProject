using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;

public class LoadingSceneController : MonoBehaviourPunCallbacks
{
    [Header("애니메이션")]
    [SerializeField] private LoadingBGSlideAni bgSlide;

    [Header("정렬")]
    [Tooltip("로딩 커튼이 다른 모든 UI(다음 씬 포함) 위에 그려지도록 하는 캔버스 정렬 순서(클수록 위).")]
    [SerializeField] private int loadingSortingOrder = 32000;

    [Header("전환 부드럽게")]
    [Tooltip("도착 씬이 로드된 뒤, 슬라이드아웃(센터→오른쪽)을 시작하기 전에 커튼을 '정지 상태'로 더 붙잡는 시간. " +
             "도착 씬의 무거운 초기화(스폰/타일 등)로 프레임이 튀는 구간을 정지된 커튼 뒤에 숨겨, 슬라이드아웃이 " +
             "그 끊김과 겹치지 않고 매끄럽게 나가게 한다.")]
    [SerializeField] private float settleAfterLoad = 0.35f;

    [Tooltip("완전판(출발 씬 스폰)에서 슬라이드인(왼→센터)을 시작하기 '전' 유예. 커튼 프리팹 인스턴스화로 " +
             "프레임이 튀는 구간을 넘긴 뒤 등장시켜, 슬라이드인이 그 히칭과 겹치지 않게 한다. 유예 중엔 커튼이 " +
             "화면 밖(leftPos)이라 출발 씬이 그대로 보인다(검은 화면 없음).")]
    [SerializeField] private float departureSlideInDelay = 0.15f;

    // [통합] 예전엔 '최소 표시시간'을 컨트롤러의 minDisplayTime과 커튼 애니의 holdSeconds 두 곳에서
    // 따로 관리했다. 이제는 **활성 패널(toGamePanel/toMainOrResultPanel)의 LoadingBGSlideAni.holdSeconds**
    // 하나로 통일한다 — 이 값이 (a)커튼 최소 표시시간이자 (b)게임/로컬 전환에서 다음 씬 로드를 미루는
    // 기준(loadAfter)이다. 패널마다 holdSeconds가 달라도 전환에 맞는 값이 자동으로 쓰인다.
    // 커튼 애니가 아예 없는 예외 경우에만 아래 FALLBACK_HOLD_SECONDS를 쓴다.
    private const float FALLBACK_HOLD_SECONDS = 2f;

    [Header("모드별 조작 팁")]
    [Tooltip("Push 모드로 입장할 때 활성화할 키 설명 패널")]
    [SerializeField] private GameObject pushModeTipPanel;
    [Tooltip("Absorb 모드로 입장할 때 활성화할 키 설명 패널")]
    [SerializeField] private GameObject absorbModeTipPanel;

    [Header("전환 방향별 로딩 패널")]
    [Tooltip("메인→게임(게임 씬으로 입장)일 때 켤 패널. 슬라이드만 들어간 패널을 연결.")]
    [SerializeField] private GameObject toGamePanel;
    [Tooltip("게임에서 빠져나올 때(결과/메인 복귀)일 때 켤 패널. 슬라이드+페이드가 들어간 패널을 연결.")]
    [SerializeField] private GameObject toMainOrResultPanel;

    // ─────────────────────────────────────────────────────────
    // 다음 씬 지정 (Loading 씬 진입 전에 설정)
    //   • NextSceneName  : 비우면 기본값(NetworkManager.gameSceneName) — 메인→인게임용
    //   • AllClientsLoad : true면 모든 클라이언트가 직접 LoadLevel 호출(결과 씬 전환용,
    //                      마스터 탈락 시에도 데드락 없이 넘어가도록). false면 마스터만 호출.
    // ─────────────────────────────────────────────────────────
    public static string NextSceneName;
    public static bool AllClientsLoad;
    // true면 Photon 룸 동기화(LoadLevel)가 아니라 로컬 SceneManager.LoadScene으로 전환한다.
    // 룸을 떠난 뒤(메인 복귀 등) 로딩 씬을 거칠 때 사용. (네트워크 게임 진입/결과는 false)
    public static bool LocalLoad;

    private string _targetScene;
    private bool _allClientsLoad;
    private bool _localLoad;
    private GameObject _activePanel;
    private bool _enteringGame; // 타겟이 게임 씬(입장)인지. 결과/메인(퇴장) 전환과 로드 타이밍을 구분.

    /// <summary>
    /// 로딩 커튼이 화면에 떠 있는 동안 true. 커튼이 걷히기 시작(ExitRoutine 진입)하면 false로 내린다.
    /// 다음 씬(특히 결과 씬)의 연출이 "커튼이 걷힌 뒤"에 시작되도록 대기 신호로 쓴다.
    /// 커튼이 아예 없는 진입(직접 로드/테스트)에서는 계속 false라 대기 없이 바로 진행된다.
    /// </summary>
    public static bool IsPresenting { get; private set; }

    /// <summary>
    /// 지금 커튼이 전환을 주도하고 있는가.
    ///
    /// ★ IsPresenting과 다르다
    ///   IsPresenting은 "커튼이 화면을 덮고 있는가"라서, 커튼이 걷히기 시작하면
    ///   false가 된다(도착 씬 연출을 시작해도 좋다는 신호). 하지만 그때도 커튼
    ///   오브젝트는 아직 살아 전환을 마무리하는 중이다.
    ///
    ///   "새 전환을 시작해도 되는가"를 물으려면 이쪽을 봐야 한다.
    ///   진행 중인데 또 시작하면 앞의 전환을 덮어써서 씬이 꼬인다.
    /// </summary>
    public static bool IsTransitioning { get { return _instance != null; } }

    private static LoadingSceneController _instance;
    private bool _targetSceneLoaded;
    private float _elapsed;
    private bool _exiting;
    private bool _nextSceneTriggered; // 추가: LoadLevel 중복 호출 방지
    private LoadingBGSlideAni _slide;  // 현재 커튼 애니(있으면 이 애니가 나가는 타이밍을 스스로 판단)
    private float _targetLoadedElapsed = -1f; // 도착 씬이 로드된 시점의 _elapsed(정착 대기 계산용)

    // ── 완전판(출발 씬 슬라이드인) ───────────────────────────────
    // Resources에 이 이름의 커튼 프리팹이 있으면, '출발 씬'에서 커튼을 미리 띄워 왼->센터 슬라이드인을
    // 재생한 뒤(끊김 없는 등장) 도착 씬을 로드한다. 프리팹이 없으면 기존 방식(Loading 씬에서 커튼 생성)으로 폴백.
    // 프리팹 만드는 법: Loading 씬의 'Canvas' 오브젝트(LoadingSceneController+두 패널 포함)를
    //   Assets/Resources/ 로 드래그해 'LoadingCurtain.prefab'으로 저장.
    private const string CURTAIN_RESOURCE_PATH = "LoadingCurtain";
    private static bool _pendingDepartureIntro; // 다음 Instantiate가 '출발 씬 슬라이드인' 모드임을 알리는 플래그
    private bool _departureIntro;                // 이 인스턴스가 출발 씬에서 미리 떠 슬라이드인부터 하는지
    private bool _loadingRequested;             // (완전판) 슬라이드인 후 Loading 씬 로드를 이미 요청했는지
    private bool _inLoadingScene;               // (완전판) 커튼이 센터 상태로 Loading 씬에 도착했는지

    private void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        _departureIntro = _pendingDepartureIntro; // 출발 씬에서 미리 띄워진 커튼인지
        _pendingDepartureIntro = false;
        DontDestroyOnLoad(gameObject);
        IsPresenting = true; // 커튼이 뜨는 순간부터 걷힐 때까지 유지
        BringLoadingCanvasToFront(); // 다음 씬 UI에 가려지지 않도록 최상단 정렬
        SceneManager.sceneLoaded += OnSceneLoaded;

        // 정적 설정값을 인스턴스로 캡처한 뒤 비워둔다 (다음 로딩 진입 시 기본값으로 복귀).
        // NextSceneName을 못 받은 클라이언트(비마스터)도 룸 권위값(GameState.CurrentGameMode)에서
        // 올바른 게임 씬을 기본값으로 잡는다. 이렇게 해야 로딩 중 마스터가 끊겨
        // OnMasterClientSwitched로 새 마스터가 된 비마스터가 잘못된 씬(예: Push인데 Absorb)을
        // 로드하는 버그를 막을 수 있다.
        _targetScene = string.IsNullOrEmpty(NextSceneName)
            ? (GameState.CurrentGameMode == GameModeType.Push
                ? PushSceneName
                : AbsorbSceneName)
            : NextSceneName;
        _allClientsLoad = AllClientsLoad;
        _localLoad = LocalLoad;
        NextSceneName = null;
        AllClientsLoad = false;
        LocalLoad = false;

        // [중요] 커튼 애니 설정은 패널을 '활성화하기 전에' 끝낸다. 패널을 SetActive하면 애니 OnEnable→Play가
        // 즉시 돌며 슬라이드인 유예(_slideInDelay)를 읽으므로, 그 전에 값이 주입돼 있어야 유예가 적용된다.
        ResolveActivePanel();

        // 활성화할 패널 안의 애니를 (비활성 포함) 미리 잡는다. 없으면 인스펙터 bgSlide.
        _slide = (_activePanel != null) ? _activePanel.GetComponentInChildren<LoadingBGSlideAni>(true) : null;
        if (_slide == null) _slide = bgSlide;
        if (_slide != null)
        {
            _slide.SetExitCondition(
                isNextSceneReady: IsTargetSettled,   // 도착 씬 로드 + 정착 대기까지 끝나야 나간다(끊김 회피)
                onExitStarted: OnCurtainExitStarted, // 커튼이 걷히기 시작 → 다음 씬 연출 진행 허용
                onExited: OnCurtainExited,           // 커튼이 완전히 빠짐 → 로딩 컨트롤러 정리
                // 완전판: 출발 씬 위에서 슬라이드인이 끝난 순간에 비로소 도착 씬을 로드한다(로드-애니 분리).
                onSlideInDone: _departureIntro ? OnDepartureSlideInDone : (System.Action)null,
                // 완전판에서만 슬라이드인 유예 적용(인스턴스화 히칭 회피). 기존 경로는 0(검은 프레임 없음).
                slideInDelay: _departureIntro ? departureSlideInDelay : 0f
            );
        }

        // 이제 패널을 실제로 켠다 → 애니 OnEnable→Play가 위에서 주입한 설정(유예 포함)으로 재생된다.
        ActivateTransitionPanels();
        ApplyModeTipPanels();
    }

    // 커튼이 센터->오른쪽으로 나가기 '시작'할 때(=화면이 드러나기 시작). 결과 씬 시퀀스는 이때부터 진행된다.
    private void OnCurtainExitStarted()
    {
        _exiting = true;      // Update의 로드/종료 처리를 멈추고, 마스터 교체 재트리거도 막는다.
        IsPresenting = false; // 결과 씬 등 다음 씬 연출 대기 해제 신호
    }

    // 커튼이 완전히 빠진 뒤 로딩 컨트롤러(및 커튼 캔버스)를 정리한다.
    private void OnCurtainExited()
    {
        Destroy(gameObject);
    }

    // [완전판] 출발 씬 위에서 슬라이드인(왼->센터)이 끝난 순간 호출.
    // 이제 '센터 상태로' Loading 씬으로 전환한다(커튼은 DontDestroy로 유지 → 센터에 정지한 채 넘어감).
    // 무거운 출발 씬 언로드는 정지한 커튼 뒤에서 진행돼 가려진다. 이후 Loading에서 hold → 도착 씬 로드 →
    // 슬라이드아웃(도착 씬 위)으로 이어진다. 즉 슬라이드인/아웃은 각각 출발/도착 씬(한가한 구간)에서만 돈다.
    private void OnDepartureSlideInDone()
    {
        if (_loadingRequested) return;
        _loadingRequested = true;
        string loadingScene = (NetworkManager.Instance != null)
            ? NetworkManager.Instance.loadingSceneName : "Loading";
        if (_localLoad)
            SceneManager.LoadScene(loadingScene);                 // 로컬 복귀: 로컬 로드
        else if (_allClientsLoad || PhotonNetwork.IsMasterClient)
            PhotonNetwork.LoadLevel(loadingScene);                // 네트워크: 마스터(또는 전 클라)만
    }

    /// <summary>
    /// 인게임(게임/결과 씬)에서 메인으로 돌아갈 때 로딩 씬을 거쳐 'toMainOrResultPanel'을 보여준다.
    /// 메인 복귀는 룸을 떠난(또는 Disconnect한) 뒤이므로 로컬 로드(LocalLoad)로 처리한다.
    /// 이미 메인/로딩 씬이면 불필요한 로딩을 피해 바로 메인으로 간다(매칭 취소·로비 등).
    /// </summary>
    public static void LoadMainViaLoading()
    {
        // 이미 커튼 전환이 진행 중이면(중복 트리거: GoToMainMenu가 먼저 시작 + OnDisconnected 재호출 등)
        // 아무것도 더 하지 않는다 → 진행 중인 슬라이드인이 폴백 LoadScene에 끊기지 않게.
        if (_instance != null) return;

        string cur = SceneManager.GetActiveScene().name;
        if (cur == "Main" || cur == "Loading")
        {
            SceneManager.LoadScene("Main");
            return;
        }

        NextSceneName = "Main";
        LocalLoad = true;

        // [완전판] 출발 씬(게임/결과)에서 커튼을 미리 띄워 왼->센터 슬라이드인을 재생한 뒤 Main을 로드한다.
        // 커튼이 센터에 정지한 채로 무거운 게임 씬 언로드가 지나가므로 슬라이드인이 끊기지 않는다.
        if (TryBeginDepartureIntro())
            return;

        // 폴백: 커튼 프리팹(Resources/LoadingCurtain)이 없으면 기존 방식(Loading 씬에서 커튼 생성).
        SceneManager.LoadScene("Loading");
    }

    /// <summary>
    /// 메인 복귀 커튼(departure-intro)을 '현재 씬'에서 즉시 시작한다 — 입장처럼 출발 씬 위에서 슬라이드인.
    /// GoToMainMenu가 Disconnect '전에' 불러, 끊김 콜백을 기다리지 않고 곧바로 출발 씬에서 슬라이드인하게 한다.
    /// 커튼이 이후 Loading→Main 로컬 전환을 주도한다. 성공(프리팹 있음) 시 true, 아니면 false(호출측 폴백).
    /// </summary>
    public static bool TryBeginReturnIntro()
    {
        if (_instance != null) return false;
        string cur = SceneManager.GetActiveScene().name;
        if (cur == "Main" || cur == "Loading") return false; // 이미 메인/로딩이면 커튼 불필요
        NextSceneName = "Main";
        LocalLoad = true;
        return TryBeginDepartureIntro();
    }

    /// <summary>
    /// Resources의 커튼 프리팹을 '출발 씬'에 띄워 슬라이드인부터 시작한다(완전판). 성공 시 true, 프리팹 없으면 false(폴백).
    /// 호출 전에 NextSceneName/AllClientsLoad/LocalLoad를 원하는 전환에 맞게 세팅해 둔다(로컬 복귀·게임 입장 공용).
    ///  • 로컬 복귀: NextSceneName="Main", LocalLoad=true 로 호출.
    ///  • 게임 입장: NextSceneName=게임씬(또는 비움→룸모드 기본값)으로 호출(마스터가). 커튼이 Loading→게임을 주도.
    /// </summary>
    public static bool TryBeginDepartureIntro()
    {
        if (_instance != null) return false;                  // 이미 커튼이 떠 있으면 중복 방지 → 폴백
        var prefab = Resources.Load<GameObject>(CURTAIN_RESOURCE_PATH);
        if (prefab == null) return false;                     // 프리팹 미설정 → 폴백
        _pendingDepartureIntro = true;
        Instantiate(prefab);                                  // Awake가 departure-intro로 셋업 + 슬라이드인 시작
        return true;
    }

    // 전환 방향에 맞는 로딩 패널을 켠다(로딩 씬은 하나로 유지하고 패널만 바꿔 끼우는 방식).
    //   • 목적지가 게임 씬(Push/Absorb)  → 메인→게임 상황 → toGamePanel (슬라이드만)
    //   • 그 외(결과 씬·메인 복귀 등)     → 게임에서 빠져나옴 → toMainOrResultPanel (슬라이드+페이드)
    // 각 패널의 애니메이션(LoadingBGSlideAni / LoadingCenterMultiAni)은 패널이 켜질 때
    // 자기 OnEnable에서 스스로 재생되므로, 여기서는 올바른 패널을 SetActive만 하면 된다.
    // 어떤 패널을 쓸지 결정만 한다(활성화는 하지 않음 — 애니 설정을 먼저 끝내기 위해 분리).
    private void ResolveActivePanel()
    {
        _enteringGame = IsGameScene(_targetScene);
        _activePanel = _enteringGame ? toGamePanel : toMainOrResultPanel;
    }

    // ═══════════════════════════════════════════════════════
    //  [LAN 이식] 게임 씬 이름을 어디서 얻는가
    // ═══════════════════════════════════════════════════════
    //
    // ★ 여기가 터진 이유
    //   원래는 NetworkManager.Instance에서 씬 이름을 읽었다. 그런데 LAN 구성에서는
    //   그 매니저를 비활성화했으므로 Instance가 null이고, Awake에서 바로
    //   NullReferenceException이 나면서 <b>로딩 씬에 갇혔다.</b>
    //
    // ★ 이름 비교 자체를 없애지 않은 이유
    //   Photon 브랜치도 이 파일을 그대로 쓴다. 그래서 매니저가 있으면 그 값을,
    //   없으면 아래 기본값을 쓴다. 씬 이름을 바꿨다면 인스펙터에서 채우면 된다.
    [Header("[LAN] 게임 씬 이름 (NetworkManager가 없을 때 사용)")]
    [SerializeField] private string lanAbsorbSceneName = "Game_io_AbsorbMode";
    [SerializeField] private string lanPushSceneName = "Game_io_PushMode";

    private string PushSceneName
    {
        get
        {
            return NetworkManager.Instance != null
                ? NetworkManager.Instance.gamePushModeSceneName
                : lanPushSceneName;
        }
    }

    private string AbsorbSceneName
    {
        get
        {
            return NetworkManager.Instance != null
                ? NetworkManager.Instance.gameAbsorbModeSceneName
                : lanAbsorbSceneName;
        }
    }

    private bool IsGameScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return sceneName == PushSceneName || sceneName == AbsorbSceneName;
    }

    // 결정된 패널을 실제로 켠다(SetActive) → 이때 애니 OnEnable→Play가 재생된다.
    private void ActivateTransitionPanels()
    {
        if (toGamePanel != null) toGamePanel.SetActive(_enteringGame);
        if (toMainOrResultPanel != null) toMainOrResultPanel.SetActive(!_enteringGame);
    }

    // 게임 씬으로 입장하는 로딩일 때만 모드별 조작 팁을 띄운다.
    // 결과 씬 전환(인게임→결과)도 같은 Loading 씬을 거치므로(_targetScene이 결과 씬),
    // 그 경우엔 조작 팁이 뜨면 어색하다 → 타겟이 게임 씬일 때로 한정한다.
    // 모드 판정은 로컬 의도(SelectedGameMode)가 아니라 룸 권위값(GameState.CurrentGameMode)을
    // 쓴다 — 위 _targetScene 결정과 동일한 기준이라 표시 팁과 실제 입장 씬이 항상 일치한다.
    private void ApplyModeTipPanels()
    {
        bool enteringGame = IsGameScene(_targetScene);

        bool isPush = GameState.CurrentGameMode == GameModeType.Push;

        if (pushModeTipPanel != null)
            pushModeTipPanel.SetActive(enteringGame && isPush);
        if (absorbModeTipPanel != null)
            absorbModeTipPanel.SetActive(enteringGame && !isPush);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Loading")
        {
            // [완전판] 출발 씬에서 슬라이드인을 마친 커튼이 '센터 상태로' Loading 씬에 도착.
            // 여기서부터 Loading hold(holdSeconds) → 도착 씬 로드 → 슬라이드아웃(도착 씬)으로 이어진다.
            if (_departureIntro && !_inLoadingScene)
            {
                _inLoadingScene = true;
                _elapsed = 0f; // Loading hold 기준을 'Loading 진입 시점'으로 리셋
            }
            return;
        }
        _targetSceneLoaded = true;
        if (_targetLoadedElapsed < 0f) _targetLoadedElapsed = _elapsed; // 정착 대기의 기준 시각
    }

    // 도착 씬이 '로드됐고' + '정착 시간(settleAfterLoad)까지 지났는지'.
    // 슬라이드아웃은 이 조건이 참일 때만 시작해, 도착 씬 초기화의 프레임 끊김과 겹치지 않는다.
    private bool IsTargetSettled()
        => _targetSceneLoaded && (_elapsed - _targetLoadedElapsed) >= settleAfterLoad;

    private void Update()
    {
        if (_exiting) return;
        _elapsed += Time.unscaledDeltaTime;

        // 다음 씬 로드 트리거.
        // 게임 씬(메인→인게임): 마스터만 LoadLevel, 나머지는 AutomaticallySyncScene으로 자동 이동
        // 결과 씬(인게임→결과): 모든 클라이언트가 각자 LoadLevel (마스터 탈락 데드락 방지)
        //
        // [중요] 네트워크 결과 전환(_allClientsLoad)만 로드를 미루지 않고 '즉시' 시작한다(loadAfter=0).
        // 그래야 결과 씬이 로딩 커튼 뒤에서 미리 준비(Start/포디움 스폰)돼, 커튼이 사라질 때 곧바로
        // 보인다(결과 카메라 시퀀스는 IsPresenting 신호로 커튼이 걷힌 뒤에 시작 — AB2).
        //
        // 반대로 게임 입장/로컬 메인복귀는 로드를 '미뤄야' 한다.
        //   • 게임 입장: 일찍 로드하면 게임 씬의 3-2-1 카운트다운(StartCountdownRoutine)이 커튼 뒤에서
        //     시작돼 일부를 놓친다. 그래서 커튼 표시시간이 끝날 무렵에 로드한다.
        //   • 로컬 메인복귀: SceneManager.LoadScene은 동기라 즉시(=loadAfter=0) 씬이 교체되고 Main UI가
        //     커튼 위로 바로 그려져 '번쩍'한다(사용자 제보). 그래서 표시시간이 지난 뒤 로드한다.
        //
        // ※ [통합] 미루는 기준을 '활성 패널 커튼의 holdSeconds'로 통일했다(옛 minDisplayTime 폐지).
        //   같은 holdSeconds가 (a)커튼 최소 표시시간이자 (b)로드 지연 기준이라, 값이 전환마다 딱 하나다.
        //   커튼이 언제 나갈지는 여전히 애니가 holdSeconds && _targetSceneLoaded(씬 준비)로 스스로 판단.
        // 도착 씬 로드 트리거.
        // [완전판] departure-intro는 '센터 상태로 Loading 씬에 도착한 뒤'(_inLoadingScene)에만 도착 씬을
        //   로드한다(그 전 Main→Loading 전환은 OnDepartureSlideInDone이 담당). born-in-Loading은 즉시(기존).
        float loadAfter = (_enteringGame || _localLoad) ? ActiveHoldSeconds() : 0f;
        bool canLoadArrival = !_departureIntro || _inLoadingScene;
        if (canLoadArrival && !_nextSceneTriggered && _elapsed >= loadAfter)
        {
            _nextSceneTriggered = true;
            if (_localLoad)
                SceneManager.LoadScene(_targetScene);       // 룸과 무관한 로컬 전환(메인 복귀 등)
            else if (_allClientsLoad || PhotonNetwork.IsMasterClient)
                PhotonNetwork.LoadLevel(_targetScene);
        }

        // 커튼 애니가 있으면 나가는 타이밍은 그 애니가 스스로 판단한다(holdSeconds && 씬 준비).
        // 애니가 없는 예외적 경우에만 컨트롤러가 직접 정리한다(폴백, FALLBACK_HOLD_SECONDS 기준).
        if (_slide == null && _targetSceneLoaded && _elapsed >= FALLBACK_HOLD_SECONDS)
        {
            _exiting = true;
            StartCoroutine(ExitRoutine());
        }
    }

    // '최소 표시시간 = 로드 지연 기준'의 단일 출처: 활성 커튼 애니의 holdSeconds.
    // 커튼 애니가 없으면(예외) 상수 폴백을 쓴다.
    private float ActiveHoldSeconds() => _slide != null ? _slide.holdSeconds : FALLBACK_HOLD_SECONDS;

    // 로딩 커튼 캔버스를 다른 모든 UI(다음 씬 UI 포함) 위에 그려지도록 최상단 정렬 순서로 올린다.
    // 로딩 씬은 DontDestroyOnLoad로 다음 씬 위에 겹쳐 있으므로, 다음 씬 캔버스에 가려지지 않으려면 필수.
    // 루트 캔버스의 sortingOrder가 오버레이 캔버스들 간 그리기 순서를 정한다(클수록 위).
    private void BringLoadingCanvasToFront()
    {
        Canvas any = GetComponent<Canvas>();
        if (any == null) any = GetComponentInChildren<Canvas>(true);
        if (any == null) any = GetComponentInParent<Canvas>();
        if (any == null)
        {
            Debug.LogWarning("[Loading] 커튼 캔버스를 찾지 못해 정렬 순서를 올리지 못했습니다.");
            return;
        }

        Canvas root = any.rootCanvas != null ? any.rootCanvas : any;
        root.sortingOrder = loadingSortingOrder;
    }

    private IEnumerator ExitRoutine()
    {
        // 커튼이 걷히기 시작하는 순간 신호를 내린다 → 결과 씬 등 다음 씬 연출이 이제부터 진행된다.
        IsPresenting = false;

        // 활성 패널을 통째로 오른쪽으로 밀어내며 나간다.
        // 패널 컨테이너 자체를 LoadingBGSlideAni의 슬라이드 대상(target)으로 두면, 패널 안 모든
        // UI(키 팁 등)가 함께 빠져 '일부만 남는' 문제가 없다. 슬라이드가 끝난 뒤 캔버스를 정리한다.
        LoadingBGSlideAni slide = null;
        if (_activePanel != null) slide = _activePanel.GetComponentInChildren<LoadingBGSlideAni>(true);
        if (slide == null) slide = bgSlide;

        float wait = 0.2f;
        if (slide != null)
        {
            slide.SkipHoldAndExit();
            wait = slide.OutDuration + 0.05f;
        }

        yield return new WaitForSecondsRealtime(wait);

        Destroy(gameObject);
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // 로컬 로드(메인 복귀)거나 모든 클라이언트 직접 로드 모드면 마스터 교체와 무관하므로 무시
        if (_localLoad || _allClientsLoad) return;
        if (!_nextSceneTriggered || _targetSceneLoaded || _exiting) return;

        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("[Loading] 새 MasterClient가 다음 씬 로드 트리거");
            PhotonNetwork.LoadLevel(_targetScene);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this)
        {
            _instance = null;
            // ExitRoutine을 거치지 않고 파괴되는 경로(중복 인스턴스/강제 정리)에서도 신호가 걸리지 않게 안전 해제.
            IsPresenting = false;
        }
    }
}
