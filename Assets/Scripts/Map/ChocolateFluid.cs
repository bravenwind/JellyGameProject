using UnityEngine;

public class ChocolateFluid : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("떠오르는 힘 (부력)")]
    public float buoyancyForce = 15f;

    [Tooltip("흐르는 힘 (유속)")]
    public float flowForce = 5f;

    [Tooltip("흐르는 방향")]
    public Vector3 flowDirection = Vector3.forward;

    [Tooltip("초콜릿의 점성 (높을수록 끈적함)")]
    public float chocolateViscosity = 3f;

    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;

        // 리지드바디가 있는 물체만 처리
        if (rb != null)
        {
            // 1. 부력 적용 (월드 기준 위쪽으로 힘을 줌)
            // 물체의 깊이에 따라 힘을 조절하면 더 리얼하지만, 간단히 상수 힘을 줍니다.
            if (other.transform.position.y < transform.position.y)
            {
                rb.AddForce(Vector3.up * buoyancyForce, ForceMode.Acceleration);
            }

            // 2. 흐름 적용 (강이 흐르는 방향으로 밀어줌)
            rb.AddForce(flowDirection.normalized * flowForce, ForceMode.Acceleration);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null)
        {
            // 물에 들어오면 저항(Drag)을 높여서 끈적하게 만듦
            rb.linearDamping = chocolateViscosity; // Unity 6 이전 버전이면 rb.drag 사용
            rb.angularDamping = chocolateViscosity; // 회전 저항도 높임
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null)
        {
            // 물에서 나가면 저항을 원래대로 (공기 저항 수준 0.05f) 복구
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
        }
    }
}