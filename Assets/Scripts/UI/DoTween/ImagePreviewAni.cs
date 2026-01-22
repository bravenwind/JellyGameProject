using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
public class ImagePreviewAni : MonoBehaviour
{
    [Header("UI")]
    public Image characterImage;
    public RectTransform target;

    [Header("Fade/Move")]
    public float fadeOutDuration = 0.12f;
    public float fadeInDuration = 0.18f;
    public float moveInDuration = 0.18f;

    public Vector2 fadeInStartOffset = new Vector2(-40f, 0f);

    [Header("Float (optional)")]
    public FloatingUI floating;

    Sequence seq;
    Vector2 basePos;

    void Awake()
    {
        if (characterImage == null) characterImage = GetComponent<Image>();
        if (target == null) target = transform as RectTransform;

        if (target != null) basePos = target.anchoredPosition;

        SetAlpha(0f);
    }

    public void Show(Sprite nextSprite)
    {
        if (characterImage == null || target == null) return;

        // 이전 트윈 정리
        KillSeq();

        if (floating != null) floating.StopFloating(true);

        Vector2 startPos = basePos + fadeInStartOffset;

        seq = DOTween.Sequence()
            .SetUpdate(true) // UI 메뉴 TimeScale 0일 가능성 고려

            .Append(characterImage.DOFade(0f, fadeOutDuration))
            // 스프라이트 교체 + 시작 위치로 이동 + 알파 0
            .AppendCallback(() =>
            {
                characterImage.sprite = nextSprite;
                target.anchoredPosition = startPos;
                SetAlpha(0f);
            })
            // 페이드 인 + 원래 위치로 이동 동시
            .Append(characterImage.DOFade(1f, fadeInDuration))
            .Join(target.DOAnchorPos(basePos, moveInDuration).SetEase(Ease.OutSine))

            .AppendCallback(() =>
            {
                if (floating != null) floating.StartFloating();
            });
    }

    public void Hide(bool stopFloating = true)
    {
        if (characterImage == null) return;

        KillSeq();

        if (stopFloating && floating != null) floating.StopFloating(true);

        seq = DOTween.Sequence()
            .SetUpdate(true)
            .Append(characterImage.DOFade(0f, fadeOutDuration));
    }

    private void KillSeq()
    {
        if (seq != null && seq.IsActive())
            seq.Kill();
        seq = null;
    }

    private void SetAlpha(float a)
    {
        Color c = characterImage.color;
        c.a = a;
        characterImage.color = c;
    }
}
