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
        _targetScene = string.IsNullOrEmpty(NextSceneName)
            ? NetworkManager.Instance.gameSceneName
            : NextSceneName;
        _allClientsLoad = AllClientsLoad;
        NextSceneName = null;
        AllClientsLoad = false;
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
        //   • 게임 씬(메인→인게임): 마스터만 LoadLevel, 나머지는 AutomaticallySyncScene으로 자동 이동
        //   • 결과 씬(인게임→결과): 모든 클라이언트가 각자 LoadLevel (마스터 탈락 데드락 방지)
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
