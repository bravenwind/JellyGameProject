using UnityEngine;
using UnityEngine.SceneManagement;
using JellyNet;

public class LoadingSceneController : MonoBehaviour
{
    [Header("애니메이션")]
    [SerializeField] private LoadingBGSlideAni bgSlide;

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
    //   • NextSceneName : 로딩 씬 진입 전에 LanSceneFlow.Begin이 채운다
    // ─────────────────────────────────────────────────────────
    public static string NextSceneName;

    private string targetScene;
    private GameObject activePanel;
    private bool targetIsGameScene; // 타겟이 게임 씬(입장)인지. 결과/메인(퇴장) 전환과 로드 타이밍을 구분.

    /// <summary>
    /// 로딩 커튼이 화면에 떠 있는 동안 true. 커튼이 걷히기 시작(OnCurtainExitStarted)하면 false로 내린다.
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
    public static bool IsTransitioning { get { return instance != null; } }

    private static LoadingSceneController instance;
    private bool targetSceneLoaded;
    private float elapsed;
    private bool nextSceneTriggered; // 씬 로드 중복 호출 방지
    private LoadingBGSlideAni activeSlide;  // 현재 커튼 애니(있으면 이 애니가 나가는 타이밍을 스스로 판단)
    private float targetLoadedElapsed = -1f; // 도착 씬이 로드된 시점의 _elapsed(정착 대기 계산용)

    // ── 커튼은 언제나 '출발 씬'에서 태어난다 ─────────────────────
    //
    // ★ 'Loading 씬에 놓인 커튼' 경로를 걷어냈다
    //   예전엔 갈래가 둘이었다. (a) 출발 씬에서 프리팹을 띄워 슬라이드인부터 하는 완전판,
    //   (b) 프리팹이 없을 때 Loading 씬에 배치된 커튼이 제자리에서 시작하는 폴백.
    //   Loading 씬에는 실제로 LoadingCurtain 프리팹 인스턴스가 놓여 있었지만,
    //   <b>그것이 주도권을 잡는 일은 한 번도 없었다.</b> (a)가 항상 성공해서
    //   커튼이 이미 DontDestroyOnLoad로 넘어와 있고, 씬의 인스턴스는 나중에 깨어나
    //   아래 Awake의 `instance != null` 가드에 걸려 그 프레임에 스스로를 파괴했다.
    //   전환할 때마다 커튼 계층 전체를 만들었다가 버린 셈이다.
    //   그래서 씬의 인스턴스를 지우고 갈래도 하나로 줄였다 —
    //   departureIntro 삼항 연산자, FALLBACK_HOLD_SECONDS, ExitRoutine이
    //   전부 도달하지 않는 (b)를 위해 남아 있던 것들이다.
    //
    //   지금 Loading 씬은 무거운 출발 씬을 커튼 뒤에서 내려놓기 위한 가벼운 경유지일
    //   뿐이고(Directional Light·EventSystem·Main Camera 셋), 커튼은 그 위를 건너간다.
    private const string CURTAIN_RESOURCE_PATH = "LoadingCurtain";
    private bool inLoadingScene;               // 커튼이 센터 상태로 Loading 씬에 도착했는지

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        IsPresenting = true; // 커튼이 뜨는 순간부터 걷힐 때까지 유지
        SceneManager.sceneLoaded += OnSceneLoaded;

        //목적지는 LanSceneFlow.Begin이 항상 채워둔다. 읽고 나서 즉시 비워
        //다음 전환에 지난 목적지가 새어 들어가지 않게 한다
        targetScene = NextSceneName;
        NextSceneName = null;

        // [중요] 커튼 애니 설정은 패널을 '활성화하기 전에' 끝낸다. 패널을 SetActive하면 애니 OnEnable→Play가
        // 즉시 돌며 슬라이드인 유예(slideInDelay)를 읽으므로, 그 전에 값이 주입돼 있어야 유예가 적용된다.
        ResolveActivePanel();

        // 활성화할 패널 안의 애니를 (비활성 포함) 미리 잡는다. 없으면 인스펙터 bgSlide.
        activeSlide = (activePanel != null) ? activePanel.GetComponentInChildren<LoadingBGSlideAni>(true) : null;
        if (activeSlide == null)
            activeSlide = bgSlide;
        if (activeSlide != null)
        {
            activeSlide.SetTransitionPlan(
                slideInDelay: departureSlideInDelay, // 등장 전 유예(인스턴스화 히칭 회피)
                onSlideInDone: OnDepartureSlideInDone,// 슬라이드인이 끝난 순간에 비로소 Loading 씬으로 넘어간다
                isNextSceneReady: IsTargetSettled,   // 도착 씬 로드 + 정착 대기까지 끝나야 나간다(끊김 회피)
                onExitStarted: OnCurtainExitStarted, // 커튼이 걷히기 시작 → 다음 씬 연출 진행 허용
                onExited: OnCurtainExited            // 커튼이 완전히 빠짐 → 로딩 컨트롤러 정리
            );
        }

        // 이제 패널을 실제로 켠다 → 애니 OnEnable→Play가 위에서 주입한 설정(유예 포함)으로 재생된다.
        ActivateTransitionPanels();
        ApplyModeTipPanels();
    }

    // 커튼이 센터->오른쪽으로 나가기 '시작'할 때(=화면이 드러나기 시작). 결과 씬 시퀀스는 이때부터 진행된다.
    private void OnCurtainExitStarted()
    {
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
    //슬라이드인 완료 알림은 LoadingBGSlideAni.PlayRoutine에서 딱 한 번 나온다
    //(패널을 켜면 OnEnable→Play가 한 번 돌고, 커튼은 DontDestroyOnLoad라 다시 켜지지 않는다).
    //중복 호출을 막던 loadingRequested 플래그는 그래서 지웠다.
    private void OnDepartureSlideInDone()
    {
        //커튼이 화면을 완전히 덮은 지금이 시간 정지를 푸는 자리다.
        //종료 연출(LanGameFlow)이 0으로 멈춰둔 것을 여기서 되돌린다 —
        //더 일찍 풀면 슬로우가 풀린 화면이 커튼 밖으로 보인다
        Time.timeScale = 1f;
        string loadingScene = LanSceneFlow.LOADING_SCENE;
        SceneManager.LoadScene(loadingScene);
    }

    /// <summary>
    /// Resources의 커튼 프리팹을 '출발 씬'에 띄워 슬라이드인부터 시작한다.
    /// 호출 전에 NextSceneName을 원하는 전환에 맞게 세팅해 둔다(로컬 복귀·게임 입장 공용).
    ///  • 로컬 복귀: NextSceneName="Main" 으로 호출.
    ///  • 게임 입장: NextSceneName=게임씬 으로 호출. 커튼이 Loading→게임 전환을 주도한다.
    ///
    /// ★ 예전엔 bool을 돌려주는 TryBegin…이었다
    ///   false를 받으면 부르는 쪽(LanSceneFlow.Begin)이 커튼 없이 Loading 씬으로
    ///   넘어가는 '폴백'을 탔다. 그런데 false가 되는 조건이 둘 다 성립하지 않았다 —
    ///   중복 호출은 Begin이 IsTransitioning으로 이미 막고 있었고, 프리팹은 항상 있다.
    ///   있지도 않은 갈래를 위해 호출부에 분기가 남아 있었고, 그 분기가 이 클래스 안의
    ///   폴백 코드 전부(FALLBACK_HOLD_SECONDS·ExitRoutine·departureIntro)를 살려두는
    ///   명분이었다. 프리팹이 사라지면 그건 배선 사고이므로 조용히 우회하지 않고 알린다.
    /// </summary>
    public static void BeginDepartureIntro()
    {
        GameObject prefab = Resources.Load<GameObject>(CURTAIN_RESOURCE_PATH);
        if (prefab == null)
        {
            Debug.LogError("[Loading] Resources/" + CURTAIN_RESOURCE_PATH + " 커튼 프리팹이 없습니다. 씬 전환을 시작할 수 없습니다.");
            return;
        }

        Instantiate(prefab);   // Awake가 셋업하고 슬라이드인을 시작한다
    }

    // 전환 방향에 맞는 로딩 패널을 켠다(로딩 씬은 하나로 유지하고 패널만 바꿔 끼우는 방식).
    //   • 목적지가 게임 씬(Push/Absorb)  → 메인→게임 상황 → toGamePanel (슬라이드만)
    //   • 그 외(결과 씬·메인 복귀 등)     → 게임에서 빠져나옴 → toMainOrResultPanel (슬라이드+페이드)
    // 각 패널의 애니메이션(LoadingBGSlideAni / LoadingCenterMultiAni)은 패널이 켜질 때
    // 자기 OnEnable에서 스스로 재생되므로, 여기서는 올바른 패널을 SetActive만 하면 된다.
    // 어떤 패널을 쓸지 결정만 한다(활성화는 하지 않음 — 애니 설정을 먼저 끝내기 위해 분리).
    private void ResolveActivePanel()
    {
        targetIsGameScene = IsGameScene(targetScene);
        activePanel = targetIsGameScene ? toGamePanel : toMainOrResultPanel;
    }

    // ═══════════════════════════════════════════════════════
    //목적지가 게임 씬인지 판별하는 데만 쓴다(입장이냐 퇴장이냐로 커튼 패널이 갈린다).
    //씬 이름을 바꿨다면 인스펙터에서 같이 고칠 것
    [Header("게임 씬 이름")]
    [SerializeField] private string lanAbsorbSceneName = "Game_io_AbsorbMode";
    [SerializeField] private string lanPushSceneName = "Game_io_PushMode";

    private bool IsGameScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return false;
        return sceneName == lanPushSceneName || sceneName == lanAbsorbSceneName;
    }

    // 결정된 패널을 실제로 켠다(SetActive) → 이때 애니 OnEnable→Play가 재생된다.
    private void ActivateTransitionPanels()
    {
        if (toGamePanel != null)
            toGamePanel.SetActive(targetIsGameScene);
        if (toMainOrResultPanel != null)
            toMainOrResultPanel.SetActive(!targetIsGameScene);
    }

    // 게임 씬으로 입장하는 로딩일 때만 모드별 조작 팁을 띄운다.
    // 결과 씬 전환(인게임→결과)도 같은 Loading 씬을 거치므로(targetScene이 결과 씬),
    // 그 경우엔 조작 팁이 뜨면 어색하다 → 타겟이 게임 씬일 때로 한정한다.
    // 모드 판정은 로컬 의도(SelectedGameMode)가 아니라 룸 권위값(GameState.CurrentGameMode)을
    // 쓴다 — 위 targetScene 결정과 동일한 기준이라 표시 팁과 실제 입장 씬이 항상 일치한다.
    private void ApplyModeTipPanels()
    {
        bool enteringGame = IsGameScene(targetScene);

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
            if (!inLoadingScene)
            {
                inLoadingScene = true;
                elapsed = 0f; // Loading hold 기준을 'Loading 진입 시점'으로 리셋
            }
            return;
        }
        targetSceneLoaded = true;
        if (targetLoadedElapsed < 0f)
            targetLoadedElapsed = elapsed; // 정착 대기의 기준 시각
    }

    // 도착 씬이 '로드됐고' + '정착 시간(settleAfterLoad)까지 지났는지'.
    // 슬라이드아웃은 이 조건이 참일 때만 시작해, 도착 씬 초기화의 프레임 끊김과 겹치지 않는다.
    private bool IsTargetSettled()
        => targetSceneLoaded && (elapsed - targetLoadedElapsed) >= settleAfterLoad;

    // ★ 예전엔 맨 위에 `if (exiting) return;` 가드가 있었다
    //   커튼이 걷히기 시작하면 Update를 통째로 멈추는 래치였는데, 한 번 true가 되면
    //   되돌리는 곳이 없었다. 되돌릴 필요도 없었다 — 곧 Destroy되니까.
    //   문제는 <b>막을 것이 남아 있지 않았다</b>는 쪽이다. 커튼이 나가려면
    //   IsTargetSettled가 참이어야 하고 그건 도착 씬이 로드된 뒤에만 참이 되는데,
    //   그 로드가 곧 nextSceneTriggered를 true로 만든다. 즉 exiting이 켜지는 시점엔
    //   아래 if가 이미 닫혀 있었다. 남은 건 elapsed를 더하는 일뿐이라 가드를 지웠다.
    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;

        // 도착 씬 로드 트리거. 커튼이 '센터 상태로 Loading 씬에 도착한 뒤'(inLoadingScene)에만 로드한다
        // — 그 전의 출발 씬→Loading 전환은 OnDepartureSlideInDone이 담당한다.
        //
        // 씬 로드는 동기라 그 프레임에 화면이 교체된다. 커튼이 다 덮이기 전에 로드하면 도착 씬 UI가
        // 커튼 위로 그려져 번쩍인다(사용자 제보). 게임 입장은 한 가지가 더 걸린다 — 일찍 로드하면
        // 게임 씬의 3-2-1 카운트다운이 커튼 뒤에서 시작돼 일부를 놓친다.
        // 그래서 둘 다 '표시 시간이 지난 뒤'로 미룬다.
        //
        // 미루는 기준은 활성 커튼의 holdSeconds 하나다(옛 minDisplayTime 폐지). 같은 값이
        // (a)커튼 최소 표시시간이자 (b)로드 지연 기준이라 전환마다 값이 딱 하나다.
        // 반대로 결과 씬은 이 로드 덕분에 커튼 뒤에서 미리 준비되고(Start·포디움 스폰),
        // 카메라 시퀀스만 IsPresenting 신호를 보고 커튼이 걷힌 뒤에 시작한다.
        if (inLoadingScene && !nextSceneTriggered && elapsed >= activeSlide.HoldSeconds)
        {
            nextSceneTriggered = true;
            SceneManager.LoadScene(targetScene);
        }

        //커튼이 언제 나갈지는 여기서 정하지 않는다 — 애니가 holdSeconds와 IsTargetSettled를
        //스스로 보고 판단하고, 다 나가면 OnCurtainExited로 알려준다
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this)
        {
            instance = null;
            // 커튼이 다 나가기 전에 파괴되는 경로(중복 인스턴스)에서도 신호가 걸린 채 남지 않게 해제.
            IsPresenting = false;
        }
    }
}
