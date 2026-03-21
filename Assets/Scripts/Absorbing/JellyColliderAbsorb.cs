using UnityEngine;
using UnityEngine.AI;

public class JellyColliderAbsorb : MonoBehaviour
{
    public Transform target;          // Player
    public float destroyDistance = 0.3f;

    public float absorbTimer = 0.0f;
    public float absorbSpeed = 30f;   // [변경] 빨려 들어가는 최고 속도
    private float completelyAbsorbedTime = 0.6f;

    private Rigidbody rb;
    public bool absorbing = false;

    public Collider edibleCollider;
    public NavMeshAgent agent;
    public WanderingAI agentAI;
    public AIWaypointPatrol patrolAI;

    [Header("Settings")]
    public GameObject spritePrefab;
    public Renderer renderer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        edibleCollider = GetComponentInChildren<Collider>();
        renderer = GetComponentInChildren<Renderer>();
        agent = GetComponentInChildren<NavMeshAgent>();
        agentAI = GetComponentInChildren<WanderingAI>();
        patrolAI = GetComponentInChildren<AIWaypointPatrol>();

        renderer.gameObject.tag = "Edible";
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.gameObject.CompareTag("PlayerMesh") && absorbing)
        {
            absorbTimer += Time.deltaTime;
            if (absorbTimer >= completelyAbsorbedTime)
            {
                OnAbsorbed();
                absorbing = false;
            }
        }
    }

    // 충돌 감지에서 호출할 함수
    public void StartAbsorb(Transform player)
    {
        if (absorbing) return;

        rb.useGravity = false;
        if (patrolAI != null) patrolAI.enabled = false;
        if (agentAI != null) agentAI.enabled = false;
        if (agent != null) agent.enabled = false;

        rb.isKinematic = false;
        edibleCollider.isTrigger = true;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        target = player;
        absorbing = true;
        absorbTimer = 0f;
    }

    void FixedUpdate()
    {
        if (!absorbing || target == null) return;

        Vector3 toTarget = target.position - transform.position;

        absorbTimer += Time.fixedDeltaTime;

        Vector3 dir = toTarget.normalized;

        float t = Mathf.Clamp01(absorbTimer / completelyAbsorbedTime);
        t = Mathf.Pow(t, 2.0f);

        float currentSpeed = Mathf.Lerp(2f, absorbSpeed, t);

        rb.linearVelocity = dir * currentSpeed;
    }

    // ... (OnAbsorbed 및 OnDrawGizmos 기존 유지) ...
    void OnAbsorbed()
    {
        PlayerAbsorber player = target.GetComponentInParent<PlayerAbsorber>();
        if (player != null)
        {
            player.AbsorbColor(GetComponent<JellyObject>().jellyType);
        }
        Destroy(gameObject);
    }

    // ... (나머지 코드 유지) ...
}