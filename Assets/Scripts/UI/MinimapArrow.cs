using UnityEngine;

public class MinimapArrow : MonoBehaviour
{
    [Header("추적할 대상 (플레이어)")]
    public Transform target;

    [Header("위치 오프셋 (높이 조절)")]
    [Tooltip("플레이어 머리 위나, 미니맵 카메라 높이에 맞게 Y값을 조절하세요.")]
    public Vector3 offset = new Vector3(0f, 2f, 0f);

    [Header("회전 동기화 설정")]
    [Tooltip("체크하면 플레이어가 넘어져도 화살표는 Y축(좌우 방향)만 회전합니다.")]
    public bool syncOnlyYRotation = true;

    private void LateUpdate()
    {
        if (target == null) return;

        // 1. 위치 동기화 (목표 위치 + 오프셋)
        // transform.position을 직접 설정하면 타겟의 스케일(Scale) 영향을 전혀 받지 않습니다.
        transform.position = target.position + offset;

        // 2. 회전 동기화
        if (syncOnlyYRotation)
        {
            // 플레이어의 Y축(바라보는 방향) 회전값만 가져와서 적용
            Vector3 targetEuler = target.eulerAngles;

            // 기존 화살표의 X, Z 회전은 유지한 채 Y축만 플레이어와 맞춥니다.
            transform.rotation = Quaternion.Euler(transform.eulerAngles.x, targetEuler.y, transform.eulerAngles.z);
        }
        else
        {
            // 플레이어의 모든 회전(기울어짐 포함)을 그대로 따라감
            transform.rotation = target.rotation;
        }
    }
}