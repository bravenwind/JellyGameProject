using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;

public class JellyColliderAbsorb : MonoBehaviour
{
    public Transform target;          // Player
    public float destroyDistance = 0.5f;

    public float absorbTimer = 0.0f;
    public float absorbSpeed = 30f;   // [변경] 빨려 들어가는 최고 속도
    private float _completelyAbsorbedTime = 0.6f;

    private Rigidbody _rb;
    public bool absorbing = false;

    public Collider edibleCollider;
    public NavMeshAgent agent;
    public WanderingAI agentAI;
    public AIWaypointPatrol patrolAI;

    [Header("Settings")]
    public GameObject spritePrefab;
    public Renderer jellyRenderer;

    public int jellyScore = 100;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        edibleCollider = GetComponentInChildren<Collider>();
        jellyRenderer = GetComponentInChildren<Renderer>();
        agent = GetComponentInChildren<NavMeshAgent>();
        agentAI = GetComponentInChildren<WanderingAI>();
        patrolAI = GetComponentInChildren<AIWaypointPatrol>();

        // NavMeshAgent가 위치를 제어하는 동안 Rigidbody가 간섭하지 않도록 kinematic으로 설정
        // (흡수 시작 시 StartAbsorb()에서 kinematic = false로 전환)
        if (agent != null)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        else
        {
            _rb.useGravity = true;
        }

        jellyRenderer.gameObject.tag = "Edible";
    }

    // 충돌 감지에서 호출할 함수
    public void StartAbsorb(Transform player)
    {
        if (absorbing) return;

        _rb.useGravity = false;
        if (patrolAI != null) patrolAI.enabled = false;
        if (agentAI != null) agentAI.enabled = false;
        if (agent != null) agent.enabled = false;

        _rb.isKinematic = false;
        edibleCollider.isTrigger = true;

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        target = player;
        absorbing = true;
        absorbTimer = 0f;
    }

    void FixedUpdate()
    {
        if (!absorbing || target == null) return;

        Vector3 toTarget = target.position - transform.position;
        float distanceToTarget = toTarget.magnitude;

        // 💡 1. 개선된 판정: 거리가 충분히 가까워지면 즉시 흡수 (비빌 필요 없음!)
        if (distanceToTarget <= destroyDistance)
        {
            CompleteAbsorption();
            return;
        }

        absorbTimer += Time.fixedDeltaTime;

        // 💡 2. 보험용 판정: 시간이 0.6초를 초과해도 무조건 흡수
        if (absorbTimer >= _completelyAbsorbedTime)
        {
            CompleteAbsorption();
            return;
        }

        // ── 빨려 들어가는 연산 ──
        Vector3 dir = toTarget.normalized;
        float t = Mathf.Clamp01(absorbTimer / _completelyAbsorbedTime);
        t = Mathf.Pow(t, 2.0f);

        float currentSpeed = Mathf.Lerp(2f, absorbSpeed, t);
        _rb.linearVelocity = dir * currentSpeed;
    }

    // 흡수 완료 시 처리할 내용을 별도 함수로 분리
    private void CompleteAbsorption()
    {
        absorbing = false;
        OnAbsorbed();
    }

    void OnAbsorbed()
    {
        PlayerAbsorber player = target.GetComponentInParent<PlayerAbsorber>();
        if (player != null)
        {
            player.AbsorbColor(GetComponent<JellyObject>().jellyType);
        }

        AIPlayerSync aiPlayerSync = target.GetComponentInParent<AIPlayerSync>();
        if (aiPlayerSync != null)
        {
            aiPlayerSync.AddScore(jellyScore);
        }

        if (NetworkJellyManager.Instance != null)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
            NetworkJellyManager.Instance.RequestDestroyJelly(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}