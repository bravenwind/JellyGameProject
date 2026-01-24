using DG.Tweening;
using System;
using UnityEngine;
using System.Collections; // 추가
using UnityEngine.SceneManagement; // 추가

public class LoadingBGSlideAni : MonoBehaviour
{
    [Header("Target UI")]
    [SerializeField] private RectTransform target;

    [Header("Positions (AnchoredPosition)")]
    [SerializeField] private Vector2 leftPos = new Vector2(-1200f, 0f);
    [SerializeField] private Vector2 centerPos = new Vector2(0f, 0f);
    [SerializeField] private Vector2 rightPos = new Vector2(1200f, 0f);

    [Header("Timings")]
    [SerializeField] private float inDuration = 0.35f;     // 왼->센터 이동 시간
    [SerializeField] private float holdSeconds = 2.5f;     // 센터에서 로딩 대기 시간
    [SerializeField] private float outDuration = 0.35f;    // 센터->오른쪽 이동 시간

    [Header("Ease")]
    [SerializeField] private Ease inEase = Ease.OutCubic;
    [SerializeField] private Ease outEase = Ease.InCubic;

    [Header("Options")]
    [SerializeField] private bool ignoreTimeScale = true;
    [SerializeField] private bool deactivateAfterOut = false; // 나가고 비활성화할지

    private Sequence seq;

    private void Reset()
    {
        target = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (target == null) target = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        Play();
    }
    
    private void OnDisable()
    {
        Kill();
    }

    public void Play(Action onComplete = null)
    {
        if (target == null) target = GetComponent<RectTransform>();
        if (target == null) return;

        Kill();

        // 시작 위치 세팅
        target.anchoredPosition = leftPos;

        seq = DOTween.Sequence();
        if (ignoreTimeScale) seq.SetUpdate(true);

        // 왼->센터
        seq.Append(target.DOAnchorPos(centerPos, inDuration).SetEase(inEase));

        // 센터에서 대기(로딩)
        seq.AppendInterval(holdSeconds);

        // 센터->오른쪽
        seq.Append(target.DOAnchorPos(rightPos, outDuration).SetEase(outEase));

        if (deactivateAfterOut)
        {
            seq.OnComplete(() =>
            {
                if (gameObject != null)
                    gameObject.SetActive(false);
            });
        }
    }

    public void Kill()
    {
        if (seq != null && seq.IsActive())
            seq.Kill();
        seq = null;
    }

    // 외부에서 로딩 시간이 끝났을 때 바로 빼고 싶으면 호출
    public void SkipHoldAndExit(float customOutDuration = -1f)
    {
        if (target == null) return;
        if (seq == null || !seq.IsActive()) return;

        float d = (customOutDuration > 0f) ? customOutDuration : outDuration;

        seq.Kill();
        seq = DOTween.Sequence();
        if (ignoreTimeScale) seq.SetUpdate(true);

        // 현재 위치에서 오른쪽으로 바로 나가기
        seq.Append(target.DOAnchorPos(rightPos, d).SetEase(outEase));

        if (deactivateAfterOut)
        {
            seq.OnComplete(() =>
            {
                if (gameObject != null)
                    gameObject.SetActive(false);
            });
        }
    }

    // ========================================================================
    // [추가된 기능] : 애니메이션 진행 중 씬 로딩을 수행하는 전용 함수
    // ========================================================================
    public void LoadSceneWithSlide(string sceneName, Action onComplete = null)
    {
        if (target == null) target = GetComponent<RectTransform>();
        if (target == null) return;

        Kill();

        // 씬이 넘어가도 로딩 UI 캔버스가 파괴되지 않도록 설정
        DontDestroyOnLoad(transform.root.gameObject);

        // 시작 위치 세팅 (왼쪽)
        target.anchoredPosition = leftPos;

        seq = DOTween.Sequence();
        if (ignoreTimeScale) seq.SetUpdate(true);

        // 1. 왼 -> 센터 이동 (화면 가리기)
        seq.Append(target.DOAnchorPos(centerPos, inDuration).SetEase(inEase));

        // 2. 이동이 완료되면 씬 로딩 코루틴 실행
        seq.OnComplete(() =>
        {
            StartCoroutine(LoadSceneCoroutine(sceneName, onComplete));
        });
    }

    private IEnumerator LoadSceneCoroutine(string sceneName, Action onComplete = null)
    {
        // 로딩이 너무 빠를 때 화면이 깜빡이는 것을 방지하기 위한 최소 대기 시간
        float minHoldTime = 1.0f;
        float timer = 0f;

        // 비동기 씬 로딩 시작
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false; // 로딩 다 되어도 자동 전환 금지

        // 로딩 진행 및 최소 대기 시간 체크
        while (asyncLoad.progress < 0.9f || timer < minHoldTime)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        // 3. 로딩 완료, 씬 활성화
        asyncLoad.allowSceneActivation = true;

        // 씬이 완전히 뜰 때까지 대기
        yield return null;

        // [수정됨] 스스로 오른쪽으로 빠지거나 파괴하지 않고, 중앙에 멈춘 채로 종료
        onComplete?.Invoke();
    }
}
