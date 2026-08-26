using DG.Tweening;
using UnityEngine;

/// <summary>
/// UI를 둥실둥실 떠 있게 하는 연출.
///
/// ═══════════════════════════════════════════════════════
///  ★ 고친 문제 세 가지
/// ═══════════════════════════════════════════════════════
///
///  ① <b>남의 트윈까지 죽였다</b>
///     Stop()이 target.DOKill()을 불렀다. 이건 "이 트랜스폼에 걸린 모든 트윈"을
///     죽인다 — 다른 스크립트가 걸어둔 슬라이드·팝 연출까지 함께 사라진다.
///     로비 패널처럼 같은 RectTransform을 여러 스크립트가 건드리는 곳에서
///     패널이 도중에 멈추거나 엉뚱한 자리에 굳는 원인이 된다.
///     → 자기 트윈에 id를 달아 그것만 죽인다.
///
///  ② <b>기준 위치가 조금씩 밀렸다</b>
///     Play()가 Stop() 직후에 CacheBase()를 불렀다. Stop()은 위치를 되돌리지
///     않으므로, 떠 있는 도중에 다시 Play()가 불리면 <b>떠 있는 좌표가 새 기준</b>이 된다.
///     그게 반복되면 UI가 화면 밖으로 서서히 걸어 나간다.
///     → 기준은 처음 한 번만 잡고, 다시 재생할 땐 먼저 기준으로 되돌린다.
///
///  ③ <b>회전이 한 바퀴 돌았다</b>
///     baseRot을 localEulerAngles로 읽으면 -2.5°가 357.5°로 온다.
///     거기에 +2.5를 더하면 360이 되어, 짧게 갸웃거려야 할 UI가 <b>한 바퀴 회전</b>한다.
///     → -180~180으로 펴서 쓴다.
///
///  덤: 진폭이 기준점 기준으로 대칭이 아니었다(base → +offset → −offset).
///      기준을 가운데 두고 위아래로 흔들도록 바꿨다.
/// </summary>
public class FloatingUIAni : MonoBehaviour
{
    [Header("Target UI (RectTransform)")]
    [SerializeField] private RectTransform target;

    [Header("Float (Position)")]
    [SerializeField] private float floatY = 18f;
    [SerializeField] private float floatX = 6f;
    [SerializeField] private float duration = 2.0f;

    [Header("Optional (Rotation)")]
    [SerializeField] private bool useRotation = true;
    [SerializeField] private float rotZ = 2.5f;

    [Header("Optional (Scale Breathing)")]
    [SerializeField] private bool useScale = false;
    [SerializeField] private float scaleAmount = 0.02f;

    [Header("Options")]
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool ignoreTimeScale = true;

    [Tooltip("여러 개가 같은 박자로 흔들리지 않게 시작 시점을 흩는다.")]
    [SerializeField] private bool randomizePhase = true;

    private Sequence seq;
    private Vector2 basePos;
    private Vector3 baseScale;
    private Vector3 baseRot;
    private bool baseCached;

    private void Reset()
    {
        target = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
        CacheBase();
    }

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    private void OnDisable()
    {
        Stop();
        RestoreBase();
    }

    private void OnDestroy()
    {
        Stop();
    }

    public void Play()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
        if (target == null)
            return;

        // ★ 순서가 중요하다: 멈추고 → 기준으로 되돌리고 → 새로 시작.
        //   되돌리지 않고 시작하면 떠 있던 자리가 새 기준이 되어 조금씩 밀린다.
        Stop();
        CacheBase();      // 처음 한 번만 실제로 잡는다
        RestoreBase();

        seq = DOTween.Sequence().SetId(this);
        if (ignoreTimeScale)
            seq.SetUpdate(true);

        // 기준을 가운데 두고 위아래로 대칭이 되게 흔든다
        Vector2 half = new Vector2(floatX, floatY) * 0.5f;
        target.anchoredPosition = basePos - half;
        seq.Append(target.DOAnchorPos(basePos + half, duration * 0.5f).SetEase(Ease.InOutSine));
        seq.SetLoops(-1, LoopType.Yoyo);

        if (useRotation)
        {
            float h = rotZ * 0.5f;
            target.localEulerAngles = baseRot + new Vector3(0f, 0f, -h);

            target.DOLocalRotate(baseRot + new Vector3(0f, 0f, h), duration * 0.5f)
                  .SetEase(Ease.InOutSine)
                  .SetLoops(-1, LoopType.Yoyo)
                  .SetUpdate(ignoreTimeScale)
                  .SetId(this);
        }

        if (useScale)
        {
            target.localScale = baseScale * (1f - scaleAmount);

            target.DOScale(baseScale * (1f + scaleAmount), duration * 0.5f)
                  .SetEase(Ease.InOutSine)
                  .SetLoops(-1, LoopType.Yoyo)
                  .SetUpdate(ignoreTimeScale)
                  .SetId(this);
        }

        // 같은 화면의 여러 UI가 한 몸처럼 움직이면 기계적으로 보인다
        if (randomizePhase && seq != null)
            seq.Goto(Random.Range(0f, duration * 0.5f), true);
    }

    public void Stop()
    {
        // ★ target.DOKill()이 아니라 자기 id만 죽인다.
        //   DOKill은 이 트랜스폼의 모든 트윈을 죽여서, 다른 스크립트가 진행 중이던
        //   슬라이드·팝 연출까지 중간에 끊어버린다.
        DOTween.Kill(this);
        seq = null;
    }

    /// <summary>기준값은 <b>처음 한 번만</b> 잡는다. 다시 잡으면 밀림이 누적된다.</summary>
    private void CacheBase()
    {
        if (baseCached || target == null)
            return;

        basePos = target.anchoredPosition;
        baseScale = target.localScale;

        // localEulerAngles는 0~360으로 감겨서 온다. -2.5°가 357.5°로 읽히면
        // +rotZ를 더했을 때 360을 넘어 한 바퀴 도는 연출이 된다.
        baseRot = NormalizeAngles(target.localEulerAngles);

        baseCached = true;
    }

    private void RestoreBase()
    {
        if (target == null || !baseCached)
            return;
        target.anchoredPosition = basePos;
        target.localScale = baseScale;
        target.localEulerAngles = baseRot;
    }

    /// <summary>0~360으로 감긴 각을 -180~180으로 편다.</summary>
    private static Vector3 NormalizeAngles(Vector3 e)
    {
        return new Vector3(Norm(e.x), Norm(e.y), Norm(e.z));
    }

    private static float Norm(float a)
    {
        a %= 360f;
        if (a > 180f)
            a -= 360f;
        if (a < -180f)
            a += 360f;
        return a;
    }

}
