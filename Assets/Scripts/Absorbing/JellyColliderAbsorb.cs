using UnityEngine;
using UnityEngine.AI;

public class JellyColliderAbsorb : MonoBehaviour
{
    public Transform target;          // Player
    public float maxForce = 100f;     // 최대 끌림 힘
    public float destroyDistance = 0.3f; // 흡수 판정 거리
    //public float lockInRadius = 3.0f; // 이 거리 안이고 시간이 지나면 강제 흡수

    public float absorbTimer = 0.0f;
    private float completelyAbsorbedTime = 0.6f;

    private Rigidbody rb;
    public bool absorbing = false;   // 중복 실행 방지

    bool lockIn = false;
    public float lockInTime = 3.0f;

    public Collider edibleCollider;
    public NavMeshAgent agent;
    public WanderingAI agentAI;
    public AIWaypointPatrol patrolAI;

    [Header("Settings")]
    public GameObject spritePrefab; // 빌보드 효과가 적용된 프리팹
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

    // ✅ [추가됨] 만약 젤리가 물리 충돌(Is Trigger 체크 해제) 상태라면 이것도 대비
    //private void OnCollisionEnter(Collision collision)
    //{
    //    Debug.Log(collision);

    //    if (collision.gameObject.CompareTag("Player") && !absorbing)
    //    {
    //        StartAbsorb(collision.transform);
    //        edibleCollider.isTrigger = true;
    //    }
    //}

    private void OnTriggerStay(Collider other)
    {
        if (other.gameObject.CompareTag("PlayerMesh") && absorbing)
        {
            absorbTimer += Time.deltaTime;
            if (absorbTimer >=  completelyAbsorbedTime) 
            {
                OnAbsorbed();   
                absorbing = false;
            }
        }
    }

    // 외부 혹은 충돌 감지에서 호출할 함수
    public void StartAbsorb(Transform player)
    {
        if (absorbing) return; // 이미 빨려가는 중이면 무시
        rb.useGravity = false;
        if (patrolAI != null) 
        {
            patrolAI.enabled = false;
        }
        if (agentAI != null)
        {
            agentAI.enabled = false;
        }
        if (agent != null)
        {
            agent.enabled = false;
        }
        rb.isKinematic = false;
        edibleCollider.isTrigger = true;

        target = player;
        absorbing = true;
        absorbTimer = 0f;

        // 닿자마자 약간 튀어 오르는 연출을 주고 싶다면 아래 주석 해제
        // rb.AddForce(Vector3.up * 5f, ForceMode.Impulse); 
    }

    void FixedUpdate()
    {
        if (!absorbing || target == null) return;

        Vector3 toTarget = target.position - transform.position;
        float dist = toTarget.magnitude;

        // 🧲 유도 단계 (멀리서 시작된 경우)
        absorbTimer += Time.fixedDeltaTime;

        Vector3 dir = toTarget.normalized;

        float t = Mathf.Clamp01(absorbTimer / completelyAbsorbedTime);
        t = Mathf.Pow(t, 2.0f);

        float force = Mathf.Lerp(0f, maxForce, t);

        rb.AddForce(dir * force, ForceMode.Force);

        return;
    }

    void OnAbsorbed()
    {

        // 부모나 충돌체에서 스크립트를 찾음
        PlayerColorAbsorb player = target.GetComponentInParent<PlayerColorAbsorb>(); // 혹은 GetComponentInParent

        if (player != null)
        {
            // Debug.Log("흡수됨");
            player.AbsorbColor(GetComponent<JellyObject>().jellyType);
            //Instantiate(spritePrefab, transform.position, Quaternion.identity);
            UIPoolManager.Instance.SpawnUI(spritePrefab.GetComponent<UIFollowTarget>(), transform);
            //UIPoolManager2.Instance.SpawnUI(player.transform);
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        // 에러 방지용 null 체크
        //var colorSource = GetComponent<JellyObject>();
        //if (colorSource != null)
        //{
        //    Gizmos.color = JellyObject.GetColor();
        //}
        Renderer renderer = gameObject.GetComponentInChildren<Renderer>();

        if (renderer != null) 
        {
            Gizmos.color = renderer.sharedMaterial.GetColor("_BaseColor");
        }

        Gizmos.DrawWireSphere(transform.position, destroyDistance);
        //Gizmos.DrawWireSphere(transform.position, lockInRadius);
    }
}