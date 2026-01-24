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

    public void Play()
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
}
