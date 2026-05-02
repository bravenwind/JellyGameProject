using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingSceneController : MonoBehaviour
{
    [Header("애니메이션")]
    [SerializeField] private LoadingBGSlideAni bgSlide;
    [SerializeField] private LoadingCenterMultiAni centerAni;

    [Header("설정")]
    [Tooltip("로딩 화면이 최소한 보여지는 시간 (너무 빨리 사라지는 것 방지)")]
    [SerializeField] private float minDisplayTime = 2f;

    private static LoadingSceneController _instance;
    private bool _targetSceneLoaded;
    private float _elapsed;
    private bool _exiting;

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

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }
}
