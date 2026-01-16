using UnityEngine;

public class JellyColliderAbsorb : MonoBehaviour
{
    public Transform target;          // Player
    public float maxForce = 100f;     // 최대 끌림 힘
    public float destroyDistance = 0.3f; // 흡수 판정 거리
    //public float lockInRadius = 3.0f; // 이 거리 안이고 시간이 지나면 강제 흡수

    public float absorbTimer = 0.0f;
    private float completelyAbsorbedTime = 0.6f;

    private Rigidbody rb;
    private bool absorbing = false;   // 중복 실행 방지

    bool lockIn = false;
    public float lockInTime = 3.0f;

    private float timer = 0.0f;

    public Collider edibleCollider;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        edibleCollider = GetComponentInChildren<Collider>();
    }

    // ✅ [추가됨] 만약 젤리가 물리 충돌(Is Trigger 체크 해제) 상태라면 이것도 대비
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player") && !absorbing)
        {
            StartAbsorb(collision.transform);
            edibleCollider.isTrigger = true;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.gameObject.CompareTag("PlayerMesh") && absorbing)
        {
            absorbTimer += Time.deltaTime;
            if (absorbTimer >=  completelyAbsorbedTime) 
            {
                OnAbsorbed();   
            }
        }
    }

    // 외부 혹은 충돌 감지에서 호출할 함수
    public void StartAbsorb(Transform player)
    {
        if (absorbing) return; // 이미 빨려가는 중이면 무시

        target = player;
        absorbing = true;
        absorbTimer = 0f;
        rb.useGravity = false;
        edibleCollider.isTrigger = true;

        // 닿자마자 약간 튀어 오르는 연출을 주고 싶다면 아래 주석 해제
        // rb.AddForce(Vector3.up * 5f, ForceMode.Impulse); 
    }

    void FixedUpdate()
    {
        if (!absorbing || target == null) return;

        Vector3 toTarget = target.position - transform.position;
        float dist = toTarget.magnitude;

        //// 🔒 확정 흡수 모드 (시간이 지났거나, 이미 아주 가까우면 바로 락인)
        //// 충돌로 시작된 경우 이미 거리가 0에 가깝기 때문에 바로 lockIn이 될 수 있도록 조건 추가
        ////if ((absorbTime >= lockInTime))
        ////{
        ////    lockIn = true;
        ////}

        ////if (lockIn)
        ////{
        ////    if (!rb.isKinematic)
        ////    {
        ////        // 물리 끄고 강제 흡수
        ////        rb.linearVelocity = Vector3.zero;
        ////        rb.isKinematic = true;
        ////    }

        ////    transform.position = Vector3.MoveTowards(
        ////        transform.position,
        ////        target.position,
        ////        12f * Time.fixedDeltaTime
        ////    );

        ////    if (dist < destroyDistance)
        ////    {
        ////        OnAbsorbed();
        ////    }
        ////    return;
        ////}

        // 🧲 유도 단계 (멀리서 시작된 경우)
        absorbTimer += Time.fixedDeltaTime;

        Vector3 dir = toTarget.normalized;

        float t = Mathf.Clamp01(absorbTimer / completelyAbsorbedTime);
        t = Mathf.Pow(t, 2.0f);

        float force = Mathf.Lerp(0f, maxForce, t);

        rb.AddForce(dir * force, ForceMode.Force);

        if (dist < destroyDistance)
        {
            OnAbsorbed();
        }

        return;
    }

    void OnAbsorbed()
    {
        // 부모나 충돌체에서 스크립트를 찾음
        PlayerColorAbsorb player = target.GetComponentInParent<PlayerColorAbsorb>(); // 혹은 GetComponentInParent

        if (player != null)
        {
            // Debug.Log("흡수됨");
            player.AbsorbColor(GetComponent<JellyColorSource>().GetJellyColorType());
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        // 에러 방지용 null 체크
        var colorSource = GetComponent<JellyColorSource>();
        if (colorSource != null)
        {
            Gizmos.color = colorSource.GetColor();
        }

        Gizmos.DrawWireSphere(transform.position, destroyDistance);
        //Gizmos.DrawWireSphere(transform.position, lockInRadius);
    }
}