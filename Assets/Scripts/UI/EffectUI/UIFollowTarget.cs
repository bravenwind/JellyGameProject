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

    [Tooltip("크기 변화 곡선 (0~1 사이 값)")]
    // Inspector에서 편집: 처음엔 0이었다가 중간에 1.2까지 커지고 끝에서 0으로 떨어지는 커브 추천
    public AnimationCurve scaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 1.2f),
        new Keyframe(1f, 0f)
    );

    [Tooltip("투명도 변화 곡선 (0~1 사이 값)")]
    // Inspector에서 편집: 빠르게 1이 되었다가 서서히 0으로 떨어지는 커브 추천
    public AnimationCurve alphaCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.1f, 1f),
        new Keyframe(0.8f, 1f),
        new Keyframe(1f, 0f)
    );

    private Camera mainCamera;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector3 lastTargetPos; // 타겟이 사라졌을 때 마지막 위치 기억용

    void Awake()
    {
        mainCamera = Camera.main;
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    // 풀에서 꺼낼 때 호출
    public void SetTarget(Transform newTarget)
    {
        targetObj = newTarget;

        // 초기 위치 설정 (깜빡임 방지)
        if (targetObj != null)
        {
            lastTargetPos = targetObj.position;
            UpdatePosition();
        }

        // 애니메이션 코루틴 시작
        StartCoroutine(PlayEffectProcess());
    }

    // 풀로 돌아갈 때 호출 (초기화)
    public void ClearTarget()
    {
        StopAllCoroutines(); // 실행 중인 애니메이션 강제 중단
        targetObj = null;
        canvasGroup.alpha = 1f; // 다음 사용을 위해 초기값 복구
        transform.localScale = Vector3.one;
    }

    // 애니메이션 및 수명 관리 코루틴
    IEnumerator PlayEffectProcess()
    {
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = timer / duration; // 0 ~ 1 진행도

            // 1. 커브에 따른 스케일 적용
            float scaleValue = scaleCurve.Evaluate(progress);
            transform.localScale = new Vector3(scaleValue, scaleValue, 1f);

            // 2. 커브에 따른 알파값 적용
            float alphaValue = alphaCurve.Evaluate(progress);
            canvasGroup.alpha = alphaValue;

            yield return null;
        }

        // 애니메이션 종료 후 풀로 자동 반환
        UIPoolManager.Instance.ReturnUI(this);
    }

    void LateUpdate()
    {
        // 타겟이 있으면 위치 갱신, 없으면 마지막 위치 유지
        if (targetObj != null)
        {
            lastTargetPos = targetObj.position;
        }

        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (mainCamera == null) return;

        // 저장된 마지막 위치(lastTargetPos)를 기준으로 화면 좌표 변환
        Vector3 targetWorldPos = lastTargetPos + worldOffset;
        Vector3 screenPos = mainCamera.WorldToScreenPoint(targetWorldPos);

        // 카메라 뒤쪽이면 숨김 (옵션)
        if (screenPos.z < 0)
        {
            canvasGroup.alpha = 0f;
        }
        else
        {
            // 위치 이동
            screenPos.z = 0;
            transform.position = screenPos;
        }
    }
}