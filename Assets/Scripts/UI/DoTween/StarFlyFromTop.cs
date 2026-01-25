using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class StarFlyFromTop : MonoBehaviour
{
    [Header("Required")]
    public Canvas canvas;
    public Image flyPrefab;
    public Image[] stars;
    //public QuestStarUI questStarUI;

    [Header("From Top")]
    public float startYOffset = 220f;
    public float startXJitter = 60f;

    [Header("Motion")]
    public float duration = 0.35f;
    public float stagger = 0.08f;
    public Ease ease = Ease.OutCubic;

    RectTransform canvasRect;
    Camera uiCam;

    void Awake()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvasRect = canvas.transform as RectTransform;

        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCam = canvas.worldCamera;
        else
            uiCam = null;
        //questStarUI = GetComponent<QuestStarUI>();
    }

    public void StarAnimatiomPlay(int count)
    {
        if (canvas == null || flyPrefab == null || stars == null) return;

        count = Mathf.Clamp(count, 0, Mathf.Min(3, stars.Length));

        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] != null) stars[i].gameObject.SetActive(false);
        }

        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            int idx = i;
            Vector2 target = GetCanvasLocalPos(stars[idx].rectTransform);

            DOVirtual.DelayedCall(stagger * i, () => FlyOne(idx, target));
        }
    }

    void FlyOne(int index, Vector2 targetLocalPos)
    {
        if (stars[index] == null) return;

        Vector2 startPos = targetLocalPos + new Vector2(
            Random.Range(-startXJitter, startXJitter),
            startYOffset
        );

        Image fly = Instantiate(flyPrefab, canvas.transform);
        RectTransform rt = fly.rectTransform;

        rt.anchoredPosition = startPos;

        fly.gameObject.SetActive(true);

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Append(rt.DOAnchorPos(targetLocalPos, duration).SetEase(ease));
        seq.Join(rt.DOScale(1.0f, duration).SetEase(Ease.OutBack));

        seq.OnComplete(() =>
        {
            stars[index].gameObject.SetActive(true);
            //questStarUI.SetStarsByQuestCount(index + 1);
            Destroy(fly.gameObject);
        });
    }

    Vector2 GetCanvasLocalPos(RectTransform target)
    {
        if (canvasRect == null) return Vector2.zero;

        Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCam, target.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCam, out Vector2 local);
        return local;
    }
}
