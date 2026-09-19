using UnityEngine;

namespace JellyNet
{
    public class NetKnockback : MonoBehaviour
    {
        [SerializeField] private float damping = 4f;
        [SerializeField] private float stopSpeed = 0.05f;

        private Vector3 velocity;

        public bool IsBeingPushed => velocity.sqrMagnitude > stopSpeed * stopSpeed;

        public void Apply(Vector3 direction, float force)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            velocity += direction.normalized * force;
        }

        private void Update()
        {
            if (!IsBeingPushed)
            {
                velocity = Vector3.zero;
                return;
            }

            transform.position += velocity * Time.deltaTime;

            //목표가 0이라 Factor 의 1에서 빼기 전 값이 그대로 감쇠율이 된다.
            //같은 식을 두 군데서 쓰지 않도록 SmoothDamping 을 거친다
            velocity *= 1f - SmoothDamping.Factor(damping, Time.deltaTime);
        }
    }
}
