using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 넉백(밀려남) 처리. Photon판 RPC_ApplyKnockback의 자리를 대신한다.
    ///
    /// ★ 이 컴포넌트는 '소유자 화면에서만' 실제로 작동한다.
    ///   호스트가 넉백을 피격자 소유자에게만 보내기 때문이다.
    ///
    /// ★ 왜 전원에게 안 보내는가
    ///   피격자는 어차피 자기 위치를 20Hz로 전송하고 있다.
    ///   소유자만 밀려나면 그 결과가 TransformUpdate를 타고 전원에게 자동 전파된다.
    ///
    ///   만약 전원에게 넉백을 보내면:
    ///     · 각자 자기 화면에서 피격자를 밀어냄  (계산 중복)
    ///     · 그 위에 소유자가 보낸 진짜 위치가 또 덮어씀 (충돌 → 덜덜 떨림)
    ///   그래서 "권위 있는 한 명만 움직이고, 나머지는 결과만 본다"가 맞다.
    /// </summary>
    public class NetKnockback : MonoBehaviour
    {
        [Tooltip("밀린 속도가 줄어드는 비율. 클수록 빨리 멈춘다.")]
        public float damping = 4f;

        [Tooltip("이 속도 아래로 떨어지면 멈춘 것으로 본다.")]
        public float stopSpeed = 0.05f;

        Vector3 _velocity;

        public bool IsBeingPushed { get { return _velocity.sqrMagnitude > stopSpeed * stopSpeed; } }

        /// <summary>호스트가 보낸 넉백을 받는다. 수평 방향으로만 민다.</summary>
        public void Apply(Vector3 dir, float force)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            _velocity += dir.normalized * force;
        }

        void Update()
        {
            if (!IsBeingPushed) { _velocity = Vector3.zero; return; }

            transform.position += _velocity * Time.deltaTime;

            // 지수 감쇠 — 프레임률과 무관하게 같은 속도로 줄어든다
            _velocity *= Mathf.Exp(-damping * Time.deltaTime);
        }
    }
}
