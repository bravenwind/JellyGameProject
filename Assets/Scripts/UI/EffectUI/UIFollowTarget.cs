using UnityEngine;
using System.Collections;

public class UIFollowTarget : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform targetObj;
    [Tooltip("타겟 위치로부터의 오프셋")]
    public Vector3 worldOffset = new Vector3(0, 2.0f, 0);

    [Header("Animation Settings")]
    [Tooltip("전체 애니메이션 지속 시간")]
    public float duration = 1.0f;

    // ✨ 1. 원하는 최대 크기를 정할 변수 추가
    [Tooltip("애니메이션 도달 시 최대 크기 배율")]
    public float maxScale = 0.75f;

    [Tooltip("크기 변화 곡선 (0~1 사이 값)")]
    // ✨ 2. 최고점이 1이 되도록 커브 수정 (1 = maxScale을 의미하게 됨)
    public AnimationCurve scaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 1.0f), // 최고점을 1로 설정
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
    private Vector3 lastTargetPos;

    void Awake()
    {
        mainCamera = Camera.main;
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void SetTarget(Transform newTarget)
    {
        targetObj = newTarget;

        if (targetObj != null)
        {
            lastTargetPos = targetObj.position;
            UpdatePosition();
        }

        StartCoroutine(PlayEffectProcess());
    }

    public void ClearTarget()
    {
        StopAllCoroutines();
        targetObj = null;
        canvasGroup.alpha = 1f;
        transform.localScale = Vector3.one;
    }

    IEnumerator PlayEffectProcess()
    {
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = timer / duration;

            // ✨ 3. 커브 값(0~1)에 내가 정한 maxScale을 곱함
            float scaleValue = scaleCurve.Evaluate(progress) * maxScale;
            transform.localScale = new Vector3(scaleValue, scaleValue, 1f);

            float alphaValue = alphaCurve.Evaluate(progress);
            canvasGroup.alpha = alphaValue;

            yield return null;
        }

        UIPoolManager.Instance.ReturnUI(this);
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