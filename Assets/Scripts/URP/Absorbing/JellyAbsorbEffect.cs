using UnityEngine;

public class JellyAbsorbEffect : MonoBehaviour
{
    public Transform target;         // Player
    public float maxForce = 100f;     // 최대 끌림 힘
    public float destroyDistance = 0.4f;
    public float lockInRadius = 3.0f;

    public float absorbTime = 0.0f;
    private float rampUpTime = 0.6f;

    private Rigidbody rb;
    private bool absorbing;

    bool lockIn = false;
    public float lockInTime = 3.0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        //rb.linearDamping = 1.5f;   // 젤리 느낌
    }

    public void SetTarget(Transform player)
    {
        target = player;
        absorbing = true;
        if (!absorbing) 
        {
            absorbTime = 0f;
        }
    }

    void FixedUpdate()
    {
        if (!absorbing || target == null) return;

        Vector3 toTarget = target.position - transform.position;
        float dist = toTarget.magnitude;

        // 🔒 확정 흡수 모드
        if (absorbTime >= lockInTime && dist <= lockInRadius)
        {
            lockIn = true;
        }

        if (lockIn)
        {
            if (!rb.isKinematic)
            {
                // 물리 끄고 강제 흡수
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            transform.position = Vector3.MoveTowards(
                transform.position,
                target.position,
                12f * Time.fixedDeltaTime
            );

            if (dist < destroyDistance)
            {
                OnAbsorbed();
            }
            return;
        }

        // 🧲 유도 단계

        absorbTime += Time.fixedDeltaTime;

        Vector3 dir = toTarget.normalized;

        float t = Mathf.Clamp01(absorbTime / rampUpTime);
        t = Mathf.Pow(t, 2.0f);

        float force = Mathf.Lerp(0f, maxForce, t);

        rb.AddForce(dir * force, ForceMode.Force);
    }

    void OnAbsorbed()
    {
        PlayerColorAbsorb player = target.GetComponentInParent<PlayerColorAbsorb>();
        if (player != null)
        {
            Debug.Log("흡수됨");
            player.AbsorbColor(GetComponent<JellyColorSource>().GetJellyColorType());
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = GetComponent<JellyColorSource>().GetColor();
        Gizmos.DrawWireSphere(transform.position, destroyDistance);
        Gizmos.DrawWireSphere(transform.position, lockInRadius);
    }
}
