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

        if (agent != null)
            _rb.useGravity = false;
        else
            _rb.useGravity = true;

        jellyRenderer.gameObject.tag = "Edible";

        // 원격 클라이언트에서는 NavMeshAgent 비활성화 + Rigidbody kinematic 전환
        // (위치는 IPunObservable로 동기화, 물리 간섭 방지)
        PhotonView pv = GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine && agent != null)
        {
            agent.enabled = false;
            _rb.isKinematic = true;
        }
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
        // 먹힌 즉시 로컬에서 숨겨 반응성을 유지한다(연출). 실제 파괴/보상은 마스터가 판정한다.
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // [V7] 보상(성장/색/점수)을 여기서 로컬로 즉시 지급하지 않는다.
        // 예전엔 각 클라가 로컬 AbsorbColor로 즉시 보상해, 두 엔티티가 같은 젤리를 동시에 먹으면
        // 둘 다 성장하는 이중 흡수가 있었다. 이제 마스터에게 '흡수 요청'만 보내고, 마스터가 선착
        // 1명을 승자로 판정해 그 승자에게만 보상을 확정한다(RPC_ConfirmEat → AbsorbColor).
        // 봇 점수는 그 보상 경로(BotBridge.HandleScaleCompleted)에서 크기 기반으로 자동 산출.
        if (NetworkJellyManager.Instance != null)
        {
            PhotonView jellyView = GetComponent<PhotonView>();
            PhotonView eaterView = target != null ? target.GetComponentInParent<PhotonView>() : null;
            if (jellyView != null && eaterView != null)
                NetworkJellyManager.Instance.RequestEatJelly(jellyView.ViewID, eaterView.ViewID);
        }
        else
        {
            // 오프라인/싱글 폴백: 네트워크 매니저가 없으면 기존처럼 로컬 처리.
            if (target != null)
            {
                PlayerAbsorber player = target.GetComponentInParent<PlayerAbsorber>();
                player?.AbsorbColor(GetComponent<JellyObject>().jellyType);
            }
            Destroy(gameObject);
        }
    }
}