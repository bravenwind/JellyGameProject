using System.Collections;
using UnityEngine;
using System; // Action 콜백 사용

/// <summary>
/// 오브젝트를 지정 시간 동안 Z축으로 한 바퀴(360도) 회전시키는 연출 컴포넌트.
/// JellySpawnMachine이 젤리 소환 연출로 사용한다.
/// ※ 회전 축은 Z축이다 — 과거 주석이 "Y값"이라고 적혀 있어 디버깅 때 혼선을 줬다. (X11)
/// </summary>
public class Rotator : MonoBehaviour
{
    [Tooltip("회전 속도 그래프 (인스펙터에서 조절)")]
    public AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // [X11 정리] 쓰이지 않던 rotateCoroutine 필드 삭제 — null 대입만 있고 값이 채워지는
    // 경로가 없어 '중복 회전 방지' 역할을 하지 못했고, #pragma로 컴파일러 경고만 덮고 있었다.
    // 중복 회전 방지가 필요해지면 호출측에서 코루틴 핸들을 관리할 것.

    /// <summary>
    /// duration 동안 Z축을 한 바퀴 돌리고, 완료 시 onComplete를 호출한다.
    /// 호출측에서 StartCoroutine으로 실행한다.
    /// </summary>
    public IEnumerator RotateRoutine(float duration, Action onComplete)
    {
        float elapsed = 0f;

        // 현재 Z각을 시작점으로, +360도를 목표로 삼는다.
        float startZ = transform.eulerAngles.z;
        float targetZ = startZ + 360f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // 진행도(0~1)를 커브에 통과시켜 가감속을 준다.
            float t = elapsed / duration;
            float curveValue = rotationCurve.Evaluate(t);

            float currentAngle = Mathf.Lerp(startZ, targetZ, curveValue);

            Vector3 currentRot = transform.eulerAngles;
            currentRot.z = currentAngle;
            transform.eulerAngles = currentRot;

            yield return null;
        }

        // 오차 누적 없이 정확한 최종 각도로 마무리.
        Vector3 finalRot = transform.eulerAngles;
        finalRot.z = targetZ;
        transform.eulerAngles = finalRot;

        onComplete?.Invoke();
    }
}
