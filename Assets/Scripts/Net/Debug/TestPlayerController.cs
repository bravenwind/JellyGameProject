using UnityEngine;

namespace JellyNet
{
    [RequireComponent(typeof(NetIdentity))]
    public class TestPlayerController : MonoBehaviour
    {
        public float moveSpeed = 6f;

        [Header("색 구분")]
        public Color mineColor = new Color(0.3f, 0.9f, 0.4f);
        public Color otherColor = new Color(0.7f, 0.7f, 0.75f);

        [Header("방향 표시")]
        [Tooltip("캡슐은 회전해도 똑같이 생겨서 방향을 알 수 없다. 앞쪽에 작은 막대를 붙인다.")]
        public bool showNose = true;

        [Header("피격 중 조작")]
        [Tooltip("넉백으로 밀리는 동안 조작이 먹히는 비율. 0이면 완전 경직.")]
        [Range(0f, 1f)] public float controlWhilePushed = 0.3f;

        private NetIdentity id;
        private NetKnockback knockback;
        private LanPlayerState state;
        private Renderer bodyRenderer;
        private Renderer noseRenderer;
        private bool colored;

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
            knockback = GetComponent<NetKnockback>();
            state = GetComponent<LanPlayerState>();
            bodyRenderer = GetComponent<Renderer>();
            if (bodyRenderer == null)
                bodyRenderer = GetComponentInChildren<Renderer>();

            if (showNose)
                CreateNose();
        }

        private void CreateNose()
        {
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.3f, 0.6f);
            nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.6f);

            Collider c = nose.GetComponent<Collider>();
            if (c != null)
                Destroy(c);

            noseRenderer = nose.GetComponent<Renderer>();
        }

        private void Update()
        {
            if (!colored && id.OwnerId != 0)
            {
                Color c = id.IsMine ? mineColor : otherColor;
                if (bodyRenderer != null)
                    bodyRenderer.material.color = c;
                if (noseRenderer != null)
                    noseRenderer.material.color = c * 0.5f;
                colored = true;
            }

            if (!id.IsMine)
                return;

            if (state != null && state.IsOutOfPlay)
                return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            Vector3 input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude < 0.01f)
                return;

            input.Normalize();

            float speed = moveSpeed;
            if (knockback != null && knockback.IsBeingPushed)
                speed *= controlWhilePushed;

            transform.position += input * speed * Time.deltaTime;

            transform.rotation = Quaternion.LookRotation(input, Vector3.up);
        }
    }
}
