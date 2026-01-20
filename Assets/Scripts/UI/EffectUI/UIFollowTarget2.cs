using UnityEngine;
using UnityEngine.UI; // Image 컴포넌트 제어를 위해 추가
using System.Collections;

public class UIFollowTarget2 : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform targetObj;
    [Tooltip("타겟 위치로부터의 오프셋")]
    public Vector3 worldOffset = new Vector3(0, 2.0f, 0);

    [Header("Image Sequence Settings")]
    [Tooltip("순서대로 보여줄 이미지 배열")]
    public Sprite[] spriteSequence;

    [Tooltip("각 이미지가 보여지는 시간 (이미지 개수만큼 전체 시간이 늘어남)")]
    public float durationPerImage = 0.5f;

    [Header("Animation Curves")]
    [Tooltip("크기 변화 곡선 (0~1 사이 값)")]
    public AnimationCurve scaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 1.2f),
        new Keyframe(1f, 0f)
    );

    [Tooltip("투명도 변화 곡선 (0~1 사이 값)")]
    public AnimationCurve alphaCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.1f, 1f),
        new Keyframe(0.8f, 1f),
        new Keyframe(1f, 0f)
    );

    private Camera mainCamera;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Image targetImage; // 이미지를 교체할 컴포넌트
    private Vector3 lastTargetPos;

    void Awake()
    {
        mainCamera = Camera.main;
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        targetImage = GetComponent<Image>(); // Image 컴포넌트 가져오기

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Image 컴포넌트가 없으면 경고
        if (targetImage == null)
            Debug.LogError("UIFollowTarget: Image 컴포넌트가 필요합니다!");
    }

    public void SetTarget(Transform newTarget)
    {
        targetObj = newTarget;

        if (targetObj != null)
        {
            lastTargetPos = targetObj.position;
            UpdatePosition();
        }

        // 스프라이트 배열이 비어있지 않은지 확인
        if (spriteSequence != null && spriteSequence.Length > 0)
        {
            StartCoroutine(PlayEffectProcess());
        }
        else
        {
            Debug.LogWarning("UIFollowTarget: Sprite 배열이 비어있습니다. 즉시 반환합니다.");
            UIPoolManager2.Instance.ReturnUI(this);
        }
    }

    public void ClearTarget()
    {
        StopAllCoroutines();
        targetObj = null;
        canvasGroup.alpha = 1f;
        transform.localScale = Vector3.one;
    }

    // 순차적 애니메이션 코루틴
    IEnumerator PlayEffectProcess()
    {
        // 배열에 있는 모든 이미지를 순서대로 처리
        for (int i = 0; i < spriteSequence.Length; i++)
        {
            // 1. 현재 순서의 이미지로 교체
            if (targetImage != null) targetImage.sprite = spriteSequence[i];

            // 2. 애니메이션 타이머 초기화
            float timer = 0f;

            // 3. 단일 이미지 애니메이션 루프 (durationPerImage 동안 실행)
            while (timer < durationPerImage)
            {
                timer += Time.deltaTime;
                float progress = timer / durationPerImage; // 0 ~ 1 진행도

                // 스케일 적용
                float scaleValue = scaleCurve.Evaluate(progress);
                transform.localScale = new Vector3(scaleValue, scaleValue, 1f);

                // 알파값 적용
                float alphaValue = alphaCurve.Evaluate(progress);
                canvasGroup.alpha = alphaValue;

                yield return null;
            }
        }

        // 모든 이미지 순회가 끝나면 풀로 반환
        UIPoolManager2.Instance.ReturnUI(this);
    }

    void LateUpdate()
    {
        if (targetObj != null)
        {
            lastTargetPos = targetObj.position;
        }
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (mainCamera == null) return;

        Vector3 targetWorldPos = lastTargetPos + worldOffset;
        Vector3 screenPos = mainCamera.WorldToScreenPoint(targetWorldPos);

        if (screenPos.z < 0)
        {
            canvasGroup.alpha = 0f;
        }
        else
        {
            screenPos.z = 0;
            transform.position = screenPos;
        }
    }
}