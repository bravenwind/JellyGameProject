using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerScaleController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SoftBody3D softBody3D;

    private Vector3 currentScale;

    // ═════════════════════════════════════════════════════════
    //  시작 크기의 유일한 출처는 프리팹이다
    // ═════════════════════════════════════════════════════════
    //
    // ★ 예전엔 '2'가 네 군데 흩어져 있었다
    //   프리팹 localScale, DataManager의 startingScale, GameState.playerCurrentScale,
    //   그리고 여기 필드 초기화 둘. 값이 우연히 같았을 뿐 출처가 없어서,
    //   프리팹을 키우면 다른 셋이 조용히 어긋났다.
    //
    //   이제 Awake에서 transform.localScale을 읽는 이 한 줄이 기준이다.
    //   필드 초기값을 아예 두지 않는 이유도 같다 — 초기값이 있으면
    //   "프리팹을 안 읽어도 그럴듯하게 도는" 상태가 생겨 실수를 덮는다.
    //
    // ★ Start가 아니라 Awake인 이유
    //   LanPlayerState.ScaleValue·NetEntity.ScaleOf가 이 값을 읽는데,
    //   그쪽이 먼저 돌면 0을 받아 '가장 작은 젤리'로 오판된다.
    //   Awake는 모든 Start보다 먼저이므로 그 창이 닫힌다.
    public float CurrentScaleValue { get; private set; }

    private float pendingScale;
    public float PendingScale => pendingScale;

    private Queue<IEnumerator> scaleQueue = new Queue<IEnumerator>();
    private bool isScaling = false;

    private Coroutine jellyBatchCoroutine;

    // ── 크기 생애 이벤트 ──
    //
    // ★ 셋을 지웠다
    //   · OnScaleValueChanged — 구독자가 0인데 성장마다 발화했다.
    //     바로 다음 줄의 완료 알림과 같은 값·같은 시점이라 완전한 중복이었다.
    //   · OnShrinkStarted / OnScaleThresholdDown — 이 게임엔 축소가 없다.
    //
    // ★ OnScaleInit도 OnScaleSettled로 흡수했다
    //   '처음 정해졌다'와 '성장이 끝났다'는 받는 쪽에서 하는 일이 같다 —
    //   전역 크기를 갱신하고 점수·점프력을 다시 계산한다. 둘로 나눠두면
    //   한쪽에만 처리를 추가하는 실수가 나고, 실제로 그랬다(Init은 점프력을 안 세웠다).
    //   "크기가 확정됐다"는 하나의 사건으로 본다.
    public event Action<float> OnScaleSettled;
    public event Action<bool> OnGrowStarted;
    public event Action OnScaleThresholdUp;
    public event Action OnPostScalePhysics;

    private void Awake()
    {
        currentScale = transform.localScale;
        CurrentScaleValue = currentScale.x;
        pendingScale = CurrentScaleValue;
    }

    private void Start()
    {
        OnScaleSettled?.Invoke(CurrentScaleValue);
    }

    // ═════════════════════════════════════════════════════════
    //  이 게임의 크기는 한 방향으로만 간다
    // ═════════════════════════════════════════════════════════
    //
    // ★ 축소 경로를 통째로 걷어냈다
    //   예전엔 ScaleTo에 growing 매개변수가 있고 OnShrinkStarted·
    //   OnScaleThresholdDown 이벤트가 딸려 있었다. 그런데 <b>줄어드는 일이
    //   실제로는 한 번도 없었다</b> — 부르는 곳 셋이 전부 growing: true였다.
    //   (우유는 이름만 MilkScaleDecrease고 속도만 건드린다)
    //
    //   쓰지 않는 갈래가 남아 있으면 읽는 사람이 "언제 줄어들지?"를 계속 찾게 되고,
    //   그 갈래에만 버그가 숨어도 아무도 모른다. 지우는 게 정직하다.
    //
    // ★ 상·하한 클램프도 없앴다
    //   씬의 maxScale이 100이라 사실상 상한이 아니었고, 하한은 줄어들 일이
    //   없으니 애초에 닿지 않는 조건이었다.

    public void GrowByJelly()
    {
        pendingScale += DataManager.Instance.JellyScaleIncrease;

        if (jellyBatchCoroutine == null)
            jellyBatchCoroutine = StartCoroutine(BatchedJellyGrow());
    }

    // ★ playEffect를 false에서 true로 바꿨다
    //   젤리를 먹었을 때만 연출을 끄고 있었다. 원래 이유는 소리가 두 번 나서였는데,
    //   진짜 원인은 PlayerBridge.HandleGrowStarted에 <b>소유자 가드가 빠져 있던 것</b>이었고
    //   그건 따로 고쳤다. false로 막아두는 바람에 생긴 부작용이 더 컸다:
    //   원격 화면은 ApplyGrow(GrowKind.Jelly)로 젤리 흡수를 <b>이미 알고 있는데도</b>
    //   팝업을 띄우지 않아, 봇을 흡수했을 때(모두에게 보임)와 젤리를 먹었을 때
    //   (먹은 사람에게만 보임)의 연출이 서로 달랐다.
    private IEnumerator BatchedJellyGrow()
    {
        yield return null;
        jellyBatchCoroutine = null;
        QueueScaleChange(ScaleTo(pendingScale, DataManager.Instance.GrowAnimTime, playEffect: true));
    }

    public void GrowByAbsorbing(float absorbedScaleValue)
    {
        pendingScale += absorbedScaleValue * DataManager.Instance.AbsorbScalePercent;
        QueueScaleChange(ScaleTo(pendingScale, DataManager.Instance.GrowAnimTime, playEffect: true));
    }

    public void GrowByBatHit(float growth)
    {
        pendingScale += growth;
        QueueScaleChange(ScaleTo(pendingScale, 0.3f, playEffect: true));
    }

    /// <summary>
    /// 연속적인 크기를 계단으로 자른다. 카메라 줌아웃을 문턱마다 <b>한 번씩만</b> 쏘기 위한 것.
    ///
    /// <code>
    /// 크기  0 ─────── 6 ─────── 10 ─────── 14 ─────── 18 ──→
    /// tier      -1     │    0     │    1     │    2     │  3
    ///                 첫 문턱    +step      +step      +step
    /// </code>
    ///
    /// (scale - first) / step 을 내림하면 6~10은 0.x → 0, 10~14는 1.x → 1이 된다.
    /// 첫 문턱 미만을 전부 -1로 묶는 이유는, 안 그러면 작은 값에서 -1·-2로 칸이 갈려
    /// 성장 초반에 문턱을 넘은 것처럼 보이기 때문이다.
    /// </summary>
    private int GetScaleTier(float scale)
    {
        float first = DataManager.Instance.CameraZoomFirstThreshold;
        float step = DataManager.Instance.CameraZoomThresholdStep;

        if (step <= 0f)
            return 0;
        if (scale < first)
            return -1;

        return Mathf.FloorToInt((scale - first) / step);
    }

    private bool CrossedThresholdUp(float prevScale, float newScale)
    {
        if (newScale <= prevScale)
            return false;
        return GetScaleTier(newScale) > GetScaleTier(prevScale);
    }

    private IEnumerator ScaleTo(float targetValue, float duration, bool playEffect = true)
    {
        if (Mathf.Approximately(targetValue, CurrentScaleValue))
            yield break;

        float prevScale = CurrentScaleValue;
        bool hitsThresholdUp = CrossedThresholdUp(prevScale, targetValue);

        if (softBody3D != null)
            softBody3D.DisableCloth();

        OnGrowStarted?.Invoke(playEffect);

        if (hitsThresholdUp)
            OnScaleThresholdUp?.Invoke();

        //크기 값(targetValue)이 곧 균등 스케일이다. 예전엔 originalScale을 곱했는데
        //그게 Vector3.one 고정이라 곱셈에 의미가 없었다
        Vector3 startScale = currentScale;
        Vector3 targetScale = Vector3.one * targetValue;
        CurrentScaleValue = targetValue;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            currentScale = Vector3.Lerp(startScale, targetScale, t / duration);
            transform.localScale = currentScale;
            yield return null;
        }
        transform.localScale = currentScale = targetScale;

        OnScaleSettled?.Invoke(CurrentScaleValue);

        if (softBody3D != null)
            softBody3D.RequestRebuildCloth();

        OnPostScalePhysics?.Invoke();
    }

    public void QueueScaleChange(IEnumerator scaleRoutine)
    {
        scaleQueue.Enqueue(scaleRoutine);
        if (!isScaling)
            StartCoroutine(ProcessScaleQueue());
    }

    private IEnumerator ProcessScaleQueue()
    {
        isScaling = true;
        while (scaleQueue.Count > 0)
        {
            yield return StartCoroutine(scaleQueue.Dequeue());
        }
        isScaling = false;
    }

}
