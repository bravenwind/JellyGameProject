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

            velocity *= Mathf.Exp(-damping * Time.deltaTime);
        }
    }
}
