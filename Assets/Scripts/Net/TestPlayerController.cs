using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// B-3 검증용 임시 조작 스크립트. 실제 게임 로직과는 무관하다.
    ///
    /// ★ 핵심 규칙: 움직이는 건 소유자(IsMine)뿐이다.
    ///   남의 캐릭터를 내 키보드로 움직이면 안 되고,
    ///   그건 NetTransform이 받은 위치대로 따라가야 한다.
    ///   (Photon의 photonView.IsMine 가드와 같은 역할)
    /// </summary>
    [RequireComponent(typeof(NetIdentity))]
    public class TestPlayerController : MonoBehaviour
    {
        public float moveSpeed = 6f;

        [Header("색 구분")]
        public Color mineColor = new Color(0.3f, 0.9f, 0.4f);    // 내 캐릭터 = 초록
        public Color otherColor = new Color(0.7f, 0.7f, 0.75f);  // 남의 캐릭터 = 회색

        [Header("방향 표시")]
        [Tooltip("캡슐은 회전해도 똑같이 생겨서 방향을 알 수 없다. 앞쪽에 작은 막대를 붙인다.")]
        public bool showNose = true;

        [Header("피격 중 조작")]
        [Tooltip("넉백으로 밀리는 동안 조작이 먹히는 비율. 0이면 완전 경직.")]
        [Range(0f, 1f)] public float controlWhilePushed = 0.3f;

        NetIdentity _id;
        NetKnockback _knockback;
        Renderer _bodyRenderer;
        Renderer _noseRenderer;
        bool _colored;

        void Awake()
        {
            _id = GetComponent<NetIdentity>();
            _knockback = GetComponent<NetKnockback>();
            _bodyRenderer = GetComponent<Renderer>();
            if (_bodyRenderer == null) _bodyRenderer = GetComponentInChildren<Renderer>();

            if (showNose) CreateNose();
        }

        /// <summary>앞쪽을 가리키는 작은 막대를 만들어 붙인다(회전이 눈에 보이도록).</summary>
        void CreateNose()
        {
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.3f, 0.6f);
            nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.6f);

            Collider c = nose.GetComponent<Collider>();
            if (c != null) Destroy(c);      // 충돌은 필요 없다

            _noseRenderer = nose.GetComponent<Renderer>();
        }

        void Update()
        {
            // 색은 소유자가 정해진 뒤(스폰 직후) 한 번만 칠한다
            if (!_colored && _id.OwnerId != 0)
            {
                Color c = _id.IsMine ? mineColor : otherColor;
                if (_bodyRenderer != null) _bodyRenderer.material.color = c;
                if (_noseRenderer != null) _noseRenderer.material.color = c * 0.5f;   // 앞쪽은 어둡게
                _colored = true;
            }

            // ★ 내 것이 아니면 입력을 아예 받지 않는다.
            //   남의 캐릭터는 NetTransform이 받은 위치대로 움직인다.
            if (!_id.IsMine) return;

            float h = Input.GetAxisRaw("Horizontal");   // A/D, ←/→
            float v = Input.GetAxisRaw("Vertical");     // W/S, ↑/↓

            Vector3 input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude < 0.01f) return;     // 입력 없음

            input.Normalize();

            // 밀리는 동안에는 조작이 잘 안 먹힌다 → 넉백이 눈에 확실히 보인다
            float speed = moveSpeed;
            if (_knockback != null && _knockback.IsBeingPushed) speed *= controlWhilePushed;

            // 화면 기준으로 직접 이동 (A/D만 눌러도 좌우로 움직인다)
            transform.position += input * speed * Time.deltaTime;

            // 가는 방향을 바라보게 — 회전이 눈에 보이도록
            transform.rotation = Quaternion.LookRotation(input, Vector3.up);
        }
    }
}
