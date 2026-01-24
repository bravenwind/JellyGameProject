using DG.Tweening;
using UnityEngine;

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

    private Sequence seq;
    private Vector2 basePos;
    private Vector3 baseScale;
    private Vector3 baseRot;

    private void Reset()
    {
        target = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (target == null) target = GetComponent<RectTransform>();
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

    public void Play()
    {
        if (target == null) target = GetComponent<RectTransform>();
        if (target == null) return;

        Stop();
        CacheBase();

        seq = DOTween.Sequence();
        if (ignoreTimeScale) seq.SetUpdate(true);

        seq.Append(target.DOAnchorPos(basePos + new Vector2(floatX, floatY), duration * 0.5f).SetEase(Ease.InOutSine));
        seq.Append(target.DOAnchorPos(basePos + new Vector2(-floatX, -floatY), duration * 0.5f).SetEase(Ease.InOutSine));
        seq.SetLoops(-1, LoopType.Yoyo);

        if (useRotation)
        {
            target.DOLocalRotate(baseRot + new Vector3(0f, 0f, rotZ), duration * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(ignoreTimeScale);
        }

        if (useScale)
        {
            Vector3 s = baseScale * (1f + scaleAmount);
            target.DOScale(s, duration * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(ignoreTimeScale);
        }
    }

    public void Stop()
    {
        if (seq != null && seq.IsActive())
            seq.Kill();
        seq = null;

        if (target != null)
        {
            target.DOKill();
        }
    }

    private void CacheBase()
    {
        if (target == null) return;
        basePos = target.anchoredPosition;
        baseScale = target.localScale;
        baseRot = target.localEulerAngles;
    }

    private void RestoreBase()
    {
        if (target == null) return;
        target.anchoredPosition = basePos;
        target.localScale = baseScale;
        target.localEulerAngles = baseRot;
    }
}
