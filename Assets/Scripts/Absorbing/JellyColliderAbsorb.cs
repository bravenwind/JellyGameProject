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

    // ... (기존 변수들 유지) ...
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
        // ... (기존 Awake 내용 유지) ...
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
        // ... (기존 내용 유지) ...
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

    // 외부 혹은 충돌 감지에서 호출할 함수
    public void StartAbsorb(Transform player)
    {
        if (absorbing) return;

        // 1. 기존 AI 및 물리 설정 끄기
        rb.useGravity = false;
        if (patrolAI != null) patrolAI.enabled = false;
        if (agentAI != null) agentAI.enabled = false;
        if (agent != null) agent.enabled = false;

        rb.isKinematic = false;
        edibleCollider.isTrigger = true;

        // ✅ [핵심 수정 1] 흡수 시작 시 기존에 튀어가던 관성을 제거합니다.
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

        // 2. 시간 경과에 따른 가속도 계산 (기존 로직 활용)
        absorbTimer += Time.fixedDeltaTime;

        Vector3 dir = toTarget.normalized; // 방향

        // 시간이 지날수록 빠르게 (0 ~ 1 사이 값)
        float t = Mathf.Clamp01(absorbTimer / completelyAbsorbedTime);
        t = Mathf.Pow(t, 2.0f); // 2차 함수 그래프로 가속 느낌 주기

        // ✅ [핵심 수정 2] AddForce 대신 속도를 직접 제어합니다.
        // 기존 maxForce 대신 absorbSpeed 변수를 사용하여 속도를 보간합니다.
        float currentSpeed = Mathf.Lerp(2f, absorbSpeed, t); // 최소 속도 2f에서 시작하여 빨라짐

        // 젤리의 속도를 "플레이어 방향 * 현재 속도"로 고정합니다.
        // 이렇게 하면 옆으로 새지 않고 무조건 플레이어에게 직선으로 날아갑니다.
        rb.linearVelocity = dir * currentSpeed;
    }

    // ... (OnAbsorbed 및 OnDrawGizmos 기존 유지) ...
    void OnAbsorbed()
    {
        PlayerColorAbsorb player = target.GetComponentInParent<PlayerColorAbsorb>();
        if (player != null)
        {
            player.AbsorbColor(GetComponent<JellyObject>().jellyType);
            UIPoolManager.Instance.SpawnUI(spritePrefab.GetComponent<UIFollowTarget>(), transform);
        }
        Destroy(gameObject);
    }

    // ... (나머지 코드 유지) ...
}