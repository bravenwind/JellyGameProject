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

    // ─────────────────────────────────────────────────────────
    // 다음 씬 지정 (Loading 씬 진입 전에 설정)
    //   • NextSceneName  : 비우면 기본값(NetworkManager.gameSceneName) — 메인→인게임용
    //   • AllClientsLoad : true면 모든 클라이언트가 직접 LoadLevel 호출(결과 씬 전환용,
    //                      마스터 탈락 시에도 데드락 없이 넘어가도록). false면 마스터만 호출.
    // ─────────────────────────────────────────────────────────
    public static string NextSceneName;
    public static bool AllClientsLoad;

    private string _targetScene;
    private bool _allClientsLoad;

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
        NextSceneName = null;
        AllClientsLoad = false;

        ApplyModeTipPanels();
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

        // minDisplayTime이 지나면 다음 씬 로드 트리거.
        // 게임 씬(메인→인게임): 마스터만 LoadLevel, 나머지는 AutomaticallySyncScene으로 자동 이동
        // 결과 씬(인게임→결과): 모든 클라이언트가 각자 LoadLevel (마스터 탈락 데드락 방지)
        if (!_nextSceneTriggered && _elapsed >= minDisplayTime)
        {
            _nextSceneTriggered = true;
            if (_allClientsLoad || PhotonNetwork.IsMasterClient)
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
        // 로딩 UI(bgSlide)는 화면 밖으로 슬라이드되어 빠지지만, 모드 팁 패널은 그 슬라이드
        // 대상의 자식이 아니라 따로 떠 있다. 같이 정리하지 않으면 배경만 사라지고 팁 패널만
        // 다음 씬 위에 남는다(로딩 캔버스가 DontDestroyOnLoad라 더 오래 보임). 함께 숨긴다.
        if (pushModeTipPanel != null) pushModeTipPanel.SetActive(false);
        if (absorbModeTipPanel != null) absorbModeTipPanel.SetActive(false);

        if (bgSlide != null)
            bgSlide.SkipHoldAndExit();

        yield return new WaitForSecondsRealtime(0.5f);

        Destroy(gameObject);
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // 모든 클라이언트가 직접 로드하는 모드면 마스터 교체와 무관하게 각자 넘어가므로 무시
        if (_allClientsLoad) return;
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
