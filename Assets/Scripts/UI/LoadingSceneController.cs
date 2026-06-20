using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;

public class LoadingSceneController : MonoBehaviourPunCallbacks
{
    [Header("애니메이션")]
    [SerializeField] private LoadingBGSlideAni bgSlide;

    [Header("설정")]
    [Tooltip("로딩 화면이 최소한 보여지는 시간 (너무 빨리 사라지는 것 방지)")]
    [SerializeField] private float minDisplayTime = 2f;

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

    private static LoadingSceneController _instance;
    private bool _targetSceneLoaded;
    private float _elapsed;
    private bool _exiting;
    private bool _nextSceneTriggered; // 추가: LoadLevel 중복 호출 방지

    private void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        // 정적 설정값을 인스턴스로 캡처한 뒤 비워둔다 (다음 로딩 진입 시 기본값으로 복귀).
        // NextSceneName을 못 받은 클라이언트(비마스터)도 룸 권위값(GameState.CurrentGameMode)에서
        // 올바른 게임 씬을 기본값으로 잡는다. 이렇게 해야 로딩 중 마스터가 끊겨
        // OnMasterClientSwitched로 새 마스터가 된 비마스터가 잘못된 씬(예: Push인데 Absorb)을
        // 로드하는 버그를 막을 수 있다.
        _targetScene = string.IsNullOrEmpty(NextSceneName)
            ? (GameState.CurrentGameMode == GameModeType.Push
                ? NetworkManager.Instance.gamePushModeSceneName
                : NetworkManager.Instance.gameAbsorbModeSceneName)
            : NextSceneName;
        _allClientsLoad = AllClientsLoad;
        _localLoad = LocalLoad;
        NextSceneName = null;
        AllClientsLoad = false;
        LocalLoad = false;

        ApplyTransitionPanels();
        ApplyModeTipPanels();
    }

    /// <summary>
    /// 인게임(게임/결과 씬)에서 메인으로 돌아갈 때 로딩 씬을 거쳐 'toMainOrResultPanel'을 보여준다.
    /// 메인 복귀는 룸을 떠난(또는 Disconnect한) 뒤이므로 로컬 로드(LocalLoad)로 처리한다.
    /// 이미 메인/로딩 씬이면 불필요한 로딩을 피해 바로 메인으로 간다(매칭 취소·로비 등).
    /// </summary>
    public static void LoadMainViaLoading()
    {
        string cur = SceneManager.GetActiveScene().name;
        if (cur == "Main" || cur == "Loading")
        {
            SceneManager.LoadScene("Main");
            return;
        }

        NextSceneName = "Main";
        LocalLoad = true;
        SceneManager.LoadScene("Loading");
    }

    // 전환 방향에 맞는 로딩 패널을 켠다(로딩 씬은 하나로 유지하고 패널만 바꿔 끼우는 방식).
    //   • 목적지가 게임 씬(Push/Absorb)  → 메인→게임 상황 → toGamePanel (슬라이드만)
    //   • 그 외(결과 씬·메인 복귀 등)     → 게임에서 빠져나옴 → toMainOrResultPanel (슬라이드+페이드)
    // 각 패널의 애니메이션(LoadingBGSlideAni / LoadingCenterMultiAni)은 패널이 켜질 때
    // 자기 OnEnable에서 스스로 재생되므로, 여기서는 올바른 패널을 SetActive만 하면 된다.
    private void ApplyTransitionPanels()
    {
        bool enteringGame =
            _targetScene == NetworkManager.Instance.gamePushModeSceneName ||
            _targetScene == NetworkManager.Instance.gameAbsorbModeSceneName;

        _enteringGame = enteringGame;
        _activePanel = enteringGame ? toGamePanel : toMainOrResultPanel;

        if (toGamePanel != null) toGamePanel.SetActive(enteringGame);
        if (toMainOrResultPanel != null) toMainOrResultPanel.SetActive(!enteringGame);
    }

    // 게임 씬으로 입장하는 로딩일 때만 모드별 조작 팁을 띄운다.
    // 결과 씬 전환(인게임→결과)도 같은 Loading 씬을 거치므로(_targetScene이 결과 씬),
    // 그 경우엔 조작 팁이 뜨면 어색하다 → 타겟이 게임 씬일 때로 한정한다.
    // 모드 판정은 로컬 의도(SelectedGameMode)가 아니라 룸 권위값(GameState.CurrentGameMode)을
    // 쓴다 — 위 _targetScene 결정과 동일한 기준이라 표시 팁과 실제 입장 씬이 항상 일치한다.
    private void ApplyModeTipPanels()
    {
        bool enteringGame =
            _targetScene == NetworkManager.Instance.gamePushModeSceneName ||
            _targetScene == NetworkManager.Instance.gameAbsorbModeSceneName;

        bool isPush = GameState.CurrentGameMode == GameModeType.Push;

        if (pushModeTipPanel != null)
            pushModeTipPanel.SetActive(enteringGame && isPush);
        if (absorbModeTipPanel != null)
            absorbModeTipPanel.SetActive(enteringGame && !isPush);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Loading") return;
        _targetSceneLoaded = true;
    }

    private void Update()
    {
        if (_exiting) return;
        _elapsed += Time.unscaledDeltaTime;

        // 다음 씬 로드 트리거.
        // 게임 씬(메인→인게임): 마스터만 LoadLevel, 나머지는 AutomaticallySyncScene으로 자동 이동
        // 결과 씬(인게임→결과): 모든 클라이언트가 각자 LoadLevel (마스터 탈락 데드락 방지)
        //
        // [중요] 결과/메인(게임에서 퇴장) 전환은 로드를 minDisplayTime까지 미루지 않고 '즉시' 시작한다.
        // 그래야 결과 씬이 로딩 커튼 뒤에서 미리 준비(Start/카메라 시퀀스)돼, 커튼이 사라질 때 곧바로
        // 보인다. 미루면 결과 씬 로드가 LoadingCenterMultiAni의 고정 fade-out보다 늦어질 때(특히 사망
        // 비마스터 클라의 지연 로드) 커튼은 사라졌는데 결과는 아직 안 떠 '유니티 기본 빈 배경(회색)'이
        // 보인다. 게임 입장(카운트다운) 전환은 기존대로 minDisplayTime 뒤 로드한다 — 일찍 로드하면
        // 3-2-1 카운트다운이 커튼 뒤에서 시작돼 일부를 놓치기 때문. 커튼 자체는 두 경우 모두
        // minDisplayTime + 타겟 로드 완료까지 유지된다(아래 ExitRoutine 조건).
        float loadAfter = _enteringGame ? minDisplayTime : 0f;
        if (!_nextSceneTriggered && _elapsed >= loadAfter)
        {
            _nextSceneTriggered = true;
            if (_localLoad)
                SceneManager.LoadScene(_targetScene);       // 룸과 무관한 로컬 전환(메인 복귀 등)
            else if (_allClientsLoad || PhotonNetwork.IsMasterClient)
                PhotonNetwork.LoadLevel(_targetScene);
        }

        if (_targetSceneLoaded && _elapsed >= minDisplayTime)
        {
            _exiting = true;
            StartCoroutine(ExitRoutine());
        }
    }

    private IEnumerator ExitRoutine()
    {
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
        if (_instance == this) _instance = null;
    }
}
