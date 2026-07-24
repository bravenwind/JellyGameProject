using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

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
    [Tooltip("로딩 화면이 '최소한' 이 시간 동안은 떠 있어야 한다(최소 표시 시간). " +
             "실제로 나가는 시점은 이 시간이 지나고 '다음 씬이 준비된' 뒤다.")]
    public float holdSeconds = 2.5f;     // 센터에서 유지되는 최소 시간
    [SerializeField] private float outDuration = 0.35f;    // 센터->오른쪽 이동 시간

    [Header("Ease")]
    [SerializeField] private Ease inEase = Ease.OutCubic;
    [SerializeField] private Ease outEase = Ease.InCubic;

    [Header("Options")]
    [SerializeField] private bool ignoreTimeScale = true;
    [SerializeField] private bool deactivateAfterOut = false; // 나가고 비활성화할지

    // 외부(LoadingSceneController)가 슬라이드아웃 종료 타이밍을 알 수 있도록 노출
    public float OutDuration => outDuration;

    // ─────────────────────────────────────────────────────────
    // 나가는 조건 / 콜백 (LoadingSceneController가 주입)
    //   • _isNextSceneReady : 다음 씬이 준비됐는지. null이면(단독 사용/테스트) 시간 조건만으로 나간다.
    //   • _onExitStarted    : 센터->오른쪽 이동을 '시작'하는 순간(커튼이 걷히기 시작).
    //   • _onExited         : 센터->오른쪽 이동이 '끝난' 순간(커튼이 완전히 빠짐).
    // ─────────────────────────────────────────────────────────
    private Func<bool> _isNextSceneReady;
    private Action _onExitStarted;
    private Action _onExited;
    private bool _forceExit; // 외부에서 최소 대기를 건너뛰고 즉시 나가라는 요청(하위호환)

    private Coroutine _routine;
    private Sequence _moveSeq;

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
        Stop();
    }

    /// <summary>
    /// 나가는 조건과 콜백을 주입한다. 반드시 이 애니가 켜지기 직전/직후에 호출해 둔다.
    /// isNextSceneReady()가 true가 되고 holdSeconds가 지나야 센터->오른쪽으로 나간다.
    /// </summary>
    public void SetExitCondition(Func<bool> isNextSceneReady, Action onExitStarted = null, Action onExited = null)
    {
        _isNextSceneReady = isNextSceneReady;
        _onExitStarted = onExitStarted;
        _onExited = onExited;
    }

    public void Play()
    {
        if (target == null) target = GetComponent<RectTransform>();
        if (target == null) return;

        Stop();
        _forceExit = false;
        _routine = StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        // 시작 위치
        target.anchoredPosition = leftPos;

        // 1) 왼 -> 센터 (등장)
        yield return MoveTo(centerPos, inDuration, inEase);

        // 2) 센터에서 대기 — (holdSeconds 경과) && (다음 씬 준비됨) 이 둘 다 true여야 나간다.
        //    holdSeconds는 '최소 표시 시간', 씬 준비는 '조기 종료 방지'. 둘의 AND라 항상 max로 걸린다.
        float held = 0f;
        while (!_forceExit)
        {
            held += ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            bool minHoldPassed = held >= holdSeconds;
            bool nextReady = (_isNextSceneReady == null) || _isNextSceneReady();
            if (minHoldPassed && nextReady) break;
            yield return null;
        }

        // 3) 센터 -> 오른쪽 (퇴장)
        _onExitStarted?.Invoke();
        yield return MoveTo(rightPos, outDuration, outEase);

        if (deactivateAfterOut && gameObject != null)
            gameObject.SetActive(false);

        _routine = null;
        _onExited?.Invoke();
    }

    // DOTween 한 구간 이동을 코루틴으로 감싼다(timeScale=0에서도 동작하도록 unscaled).
    private IEnumerator MoveTo(Vector2 pos, float dur, Ease ease)
    {
        KillSeq();
        _moveSeq = DOTween.Sequence();
        if (ignoreTimeScale) _moveSeq.SetUpdate(true);
        _moveSeq.Append(target.DOAnchorPos(pos, dur).SetEase(ease));

        while (_moveSeq != null && _moveSeq.IsActive() && !_moveSeq.IsComplete())
            yield return null;
    }

    /// <summary>
    /// 외부에서 최소 대기(holdSeconds)와 씬 준비 조건을 건너뛰고 즉시 나가게 한다(하위호환).
    /// 현재 흐름은 SetExitCondition 기반이라 보통 쓰지 않지만, 강제 종료 경로용으로 남겨 둔다.
    /// </summary>
    public void SkipHoldAndExit(float customOutDuration = -1f)
    {
        _forceExit = true;
    }

    public void Stop()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        KillSeq();
    }

    // 하위호환: 예전 이름(Kill) 호출부가 남아 있어도 동작하도록 유지.
    public void Kill()
    {
        Stop();
    }

    private void KillSeq()
    {
        if (_moveSeq != null && _moveSeq.IsActive())
            _moveSeq.Kill();
        _moveSeq = null;
    }
}
