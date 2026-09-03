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
    [SerializeField] private float holdSeconds = 2.5f;   // 센터에서 유지되는 최소 시간
    public float HoldSeconds { get { return holdSeconds; } }
    [SerializeField] private float outDuration = 0.35f;    // 센터->오른쪽 이동 시간

    [Header("Ease")]
    [SerializeField] private Ease inEase = Ease.OutCubic;
    [SerializeField] private Ease outEase = Ease.InCubic;

    [Header("Options")]
    [SerializeField] private bool ignoreTimeScale = true;
    [SerializeField] private bool deactivateAfterOut = false; // 나가고 비활성화할지

    // 외부(LoadingSceneController)가 슬라이드아웃 종료 타이밍을 알 수 있도록 노출
    public float OutDuration => outDuration;

    // 센터->오른쪽(퇴장) 이동을 '시작'하는 순간 발생. 자식 LoadingCenterMultiAni가 이 시점에
    // Phase3(사라짐)를 재생해 BG 슬라이드와 동시에 나가도록 동기화하는 데 쓴다.
    public event Action ExitStarted;

    // ─────────────────────────────────────────────────────────
    // 전환 계획 (LoadingSceneController가 주입)
    //   재생은 [유예 → 왼→센터 → 대기 → 센터→오른쪽] 한 줄기이고, 아래 다섯은 그 줄기의
    //   각 마디에 붙는다. <b>주입받는 것은 이번 전환에 쓰이는 패널 하나뿐이다.</b>
    //   커튼 프리팹에는 전환 패널이 둘 있고 둘 다 켜진 채로 태어나므로, 쓰이지 않는 쪽도
    //   Play가 잠깐 돌았다가 SetActive(false)로 멈춘다 — 그쪽은 아무것도 주입받지 않아
    //   전부 null이다. 그래서 호출부마다 null 검사가 있다.
    //     • slideInDelay     : 왼→센터를 시작하기 '전' 유예. 인스턴스화 히칭이 지나가길 기다린다.
    //     • onSlideInDone    : 왼→센터가 '끝난' 순간. 여기서 다음 씬 로드가 시작된다.
    //     • isNextSceneReady : 다음 씬이 준비됐는지. null이면 시간 조건만으로 나간다.
    //     • onExitStarted    : 센터→오른쪽 이동을 '시작'하는 순간(커튼이 걷히기 시작).
    //     • onExited         : 센터→오른쪽 이동이 '끝난' 순간(커튼이 완전히 빠짐).
    // ─────────────────────────────────────────────────────────
    private float slideInDelay;
    private Action onSlideInDone;
    private Func<bool> isNextSceneReady;
    private Action onExitStarted;
    private Action onExited;

    private Coroutine routine;
    private Sequence moveSeq;

    private void Reset()
    {
        target = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
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
    /// 이 커튼이 <b>등장부터 퇴장까지</b> 어떻게 돌지를 한 번에 주입한다.
    /// 반드시 이 애니가 켜지기 '전'에 호출해 둔다 — 켜지는 순간 OnEnable→Play가
    /// 슬라이드인 유예부터 읽기 시작하므로, 그 뒤에 넣으면 유예가 적용되지 않는다.
    ///
    /// ★ 이름이 SetExitCondition이었다
    ///   나가는 조건만 받는 것처럼 읽히는데 실제로는 <b>들어오기 전 유예(slideInDelay)와
    ///   들어온 직후에 할 일(onSlideInDone)까지</b> 함께 받고 있었다. 그 둘은 퇴장과
    ///   아무 상관이 없다. 인자 순서도 재생 순서와 뒤집혀 있어서, 읽는 사람이
    ///   "슬라이드인 관련 인자가 왜 여기 있지"를 매번 다시 물었다.
    ///   이름을 계획 전체로 넓히고 인자를 재생 순서대로 세웠다.
    /// </summary>
    public void SetTransitionPlan(float slideInDelay = 0f, Action onSlideInDone = null,
                                  Func<bool> isNextSceneReady = null,
                                  Action onExitStarted = null, Action onExited = null)
    {
        this.slideInDelay = slideInDelay;
        this.onSlideInDone = onSlideInDone;
        this.isNextSceneReady = isNextSceneReady;
        this.onExitStarted = onExitStarted;
        this.onExited = onExited;
    }

    public void Play()
    {
        //Awake가 이미 채워뒀다. 여기서 남는 건 'RectTransform이 아예 없는 오브젝트'뿐이라
        //조용히 돌아간다 — 애니가 없는 것과 같고, 그건 배선의 문제지 재생의 문제가 아니다
        if (target == null)
            return;

        Stop();
        routine = StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        // 시작 위치
        target.anchoredPosition = leftPos;

        // [끊김 회피] 슬라이드인 시작 '전' 유예. 커튼 인스턴스화/씬 히칭이 지나가길 기다렸다가 등장한다.
        // 이 동안 커튼은 leftPos(화면 밖)라, 완전판(출발 씬 스폰)에선 출발 씬이 그대로 보인다(검은 화면 없음).
        // 유예 0(기존 born-in-Loading 경로)에선 즉시 등장 → 검은 프레임 없음.
        float pre = 0f;
        while (pre < slideInDelay)
        {
            pre += ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }

        // 1) 왼 -> 센터 (등장)
        yield return MoveTo(centerPos, inDuration, inEase);

        // 등장(왼->센터)이 끝난 순간 알림. 완전판(출발 씬 슬라이드인)에서 이 시점에 씬 로드를 시작한다
        // → 슬라이드인은 로드와 겹치지 않고(출발 씬 위에서) 끝난 뒤에 로드가 돌아 끊김이 사라진다.
        onSlideInDone?.Invoke();

        // 2) 센터에서 대기 — (holdSeconds 경과) && (다음 씬 준비됨) 이 둘 다 true여야 나간다.
        //    holdSeconds는 '최소 표시 시간', 씬 준비는 '조기 종료 방지'. 둘의 AND라 항상 max로 걸린다.
        float held = 0f;
        while (true)
        {
            held += ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            bool minHoldPassed = held >= holdSeconds;
            bool nextReady = (isNextSceneReady == null) || isNextSceneReady();
            if (minHoldPassed && nextReady)
                break;
            yield return null;
        }

        // 3) 센터 -> 오른쪽 (퇴장)
        onExitStarted?.Invoke();
        ExitStarted?.Invoke(); // 자식 센터 애니가 이때 Phase3(사라짐)를 시작 → BG와 동시 퇴장
        yield return MoveTo(rightPos, outDuration, outEase);

        if (deactivateAfterOut && gameObject != null)
            gameObject.SetActive(false);

        routine = null;
        onExited?.Invoke();
    }

    // DOTween 한 구간 이동을 코루틴으로 감싼다(timeScale=0에서도 동작하도록 unscaled).
    private IEnumerator MoveTo(Vector2 pos, float dur, Ease ease)
    {
        KillSeq();
        moveSeq = DOTween.Sequence();
        if (ignoreTimeScale)
            moveSeq.SetUpdate(true);
        moveSeq.Append(target.DOAnchorPos(pos, dur).SetEase(ease));

        while (moveSeq != null && moveSeq.IsActive() && !moveSeq.IsComplete())
            yield return null;
    }

    public void Stop()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        KillSeq();
    }

    private void KillSeq()
    {
        if (moveSeq != null && moveSeq.IsActive())
            moveSeq.Kill();
        moveSeq = null;
    }
}
