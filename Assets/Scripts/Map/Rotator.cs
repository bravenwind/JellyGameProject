using System.Collections;
using UnityEngine;
using System; // Action을 사용하기 위해 필요

public class Rotator : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("회전하는 데 걸리는 총 시간 (초)")]
    public float duration = 3.0f;

    [Tooltip("회전 속도 그래프 (인스펙터에서 조절)")]
    // 기본적으로 EaseInOut 곡선을 생성합니다.
    public AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Coroutine rotateCoroutine;

    /// <summary>
    /// 외부 스크립트에서 호출할 함수입니다.
    /// </summary>
    /// <param name="onComplete">회전이 끝난 후 실행할 콜백(선택 사항)</param>
    public void Rotate360(Action onComplete = null)
    {
        // 이미 회전 중이라면 멈추고 새로 시작 (필요에 따라 정책 변경 가능)
        if (rotateCoroutine != null) StopCoroutine(rotateCoroutine);

        rotateCoroutine = StartCoroutine(RotateRoutine(onComplete));
    }

    private IEnumerator RotateRoutine(Action onComplete)
    {
        float elapsed = 0f;

        // 현재 Y각도 저장
        float startY = transform.eulerAngles.z;
        // 목표 Y각도 (현재 + 360도)
        float targetY = startY + 360f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // 0에서 1 사이의 진행률(t) 계산
            float t = elapsed / duration;

            // 커브를 통해 가속/감속/등속 비율을 가져옴 (0 ~ 1)
            float curveValue = rotationCurve.Evaluate(t);

            // 시작 각도와 목표 각도 사이를 커브 값에 따라 보간
            float currentAngle = Mathf.Lerp(startY, targetY, curveValue);

            // 회전 적용
            Vector3 currentRot = transform.eulerAngles;
            currentRot.z = currentAngle;
            transform.eulerAngles = currentRot;

            yield return null;
        }

        // 루프 종료 후 정확한 각도로 보정
        Vector3 finalRot = transform.eulerAngles;
        finalRot.z = targetY;
        transform.eulerAngles = finalRot;

        rotateCoroutine = null;

        // 완료 후 실행할 로직이 있다면 실행
        onComplete?.Invoke();
    }
}