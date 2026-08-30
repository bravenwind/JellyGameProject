using UnityEngine;

public class TopDownCameraFollow : MonoBehaviour
{
    [Header("추적 대상 설정")]
    [Tooltip("따라다닐 플레이어 객체를 연결하세요.")]
    [SerializeField] private Transform target;
    public Transform Target { get { return target; } set { target = value; } }

    [Header("카메라 위치 설정")]
    [Tooltip("플레이어와 카메라 사이의 거리 (Y값을 높일수록 더 위에서 보게 됩니다)")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 25f, -10f); // Y: 25(높이), Z: -10(뒤쪽)

    [Header("카메라 이동 설정")]
    [Tooltip("카메라가 따라가는 속도 (값이 낮을수록 무겁고 부드럽게 따라감)")]
    [Range(1f, 10f)]
    public float smoothSpeed = 5f;

    private void LateUpdate()
    {
        // 타겟이 없으면 실행하지 않음
        if (target == null)
            return;

        // 1. 카메라가 이동해야 할 목표 위치 계산
        Vector3 desiredPosition = target.position + offset;

        // 2. 현재 위치에서 목표 위치로 부드럽게 보간(Lerp)
        Vector3 smoothedPosition = Vector3.Lerp(
            transform.position, desiredPosition, SmoothDamping.Factor(smoothSpeed, Time.deltaTime));

        // 3. 카메라 위치 적용
        transform.position = smoothedPosition;

        // 4. (중요) 아주 위에서 내려다보므로, 항상 플레이어를 향해 회전하도록 설정
        //
        //transform.LookAt(target);
    }
}