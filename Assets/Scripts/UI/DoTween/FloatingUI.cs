using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class FloatingUI : MonoBehaviour
{
    [Header("Target (optional)")]
    public RectTransform target;

    [Header("Float Settings")]
    public float amplitude = 12f;
    public float duration = 2.5f;

    Tween tween;
    Vector2 basePos;

    void Awake()
    {
        if (target == null)
            target = transform as RectTransform;
        basePos = target.anchoredPosition;
    }

    public void StartFloating()
    {
        if (target == null)
            return;

        StopFloating();

        basePos = target.anchoredPosition;

        tween = target
            .DOAnchorPosY(basePos.y + amplitude, duration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    public void StopFloating(bool resetPosition = true)
    {
        if (tween != null && tween.IsActive())
            tween.Kill();

        tween = null;

        if (resetPosition && target != null)
            target.anchoredPosition = basePos;
    }
}
