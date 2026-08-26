using UnityEngine;
using DG.Tweening;

public class UIDropBounce : MonoBehaviour
{
    [SerializeField] private RectTransform target;
    [SerializeField] private float startY = 900f;
    [SerializeField] private float duration = 1f;
    [SerializeField] private Ease ease = Ease.OutBounce;
    [SerializeField] private bool ignoreTimeScale = true;

    private Vector2 endPos;
    private Tween tween;

    private void Awake()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
        endPos = target.anchoredPosition;
    }

    private void OnEnable()
    {
        Play();
    }

    public void Play()
    {
        tween?.Kill();

        // start above
        target.anchoredPosition = new Vector2(endPos.x, startY);

        // drop + bounce
        tween = target.DOAnchorPosY(endPos.y, duration)
                       .SetEase(ease)
                       .SetUpdate(ignoreTimeScale);
    }
}
