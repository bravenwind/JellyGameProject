using UnityEngine;

public class PushObject : MonoBehaviour // 본인의 클래스 이름과 동일하게 유지하세요.
{
    // 밀어내는 힘의 세기 (유니티 인스펙터 창에서 조절 가능)
    public float pushPower = 2.0f;

    // 캐릭터 컨트롤러가 다른 콜라이더와 부딪힐 때마다 자동으로 실행되는 함수
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.gameObject.CompareTag("Sphere"))
        {
            Rigidbody body = hit.collider.attachedRigidbody;

            // 1. 부딪힌 오브젝트에 리지드바디가 없거나, Kinematic(물리무시) 상태라면 무시
            if (body == null || body.isKinematic)
            {
                return;
            }

            // 2. 캐릭터가 공 위에 올라타서 밟았을 때 공이 땅 밑으로 꺼지는 것 방지
            if (hit.moveDirection.y < -0.3f)
            {
                return;
            }

            // 3. 밀어낼 방향 계산 (Y축은 제외하고 수평으로만 밀기 위함)
            Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);

            // 4. 공에 힘을 가해서 밀어냄 (순간적인 힘을 가함)
            body.AddForce(pushDir * pushPower, ForceMode.Impulse);
        }
    }
}