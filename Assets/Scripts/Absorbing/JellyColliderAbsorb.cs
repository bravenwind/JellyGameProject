using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using JellyNet;

public class JellyColliderAbsorb : MonoBehaviour, INetPoolable
{
    public Transform target;          // Player

    [Header("흡수 연출")]
    [Tooltip("빨려 들어가는 속도. 이동 거리를 이 값으로 나눠 연출 시간을 구한다.")]
    public float absorbSpeed = 30f;

    [Tooltip("연출 시간의 하한(초). 코앞에서 먹혀도 최소 이만큼은 보여준다.")]
    public float minAbsorbTime = 0.15f;

    [Tooltip("연출 시간의 상한(초). 멀리서 시작해도 이 이상 끌지 않는다.")]
    public float maxAbsorbTime = 0.5f;

    [Tooltip("끝났을 때 남는 크기 비율. 작을수록 완전히 빨려 들어간 것처럼 보인다.")]
    [Range(0f, 0.3f)] public float endScaleRatio = 0.05f;

    public float absorbTimer = 0.0f;

    private float absorbDuration = 0.3f;
    private Vector3 absorbStartPos;
    private Vector3 absorbStartScale;
    private Vector3 spawnScale;

    private Rigidbody _rb;
    public bool absorbing = false;

    public Collider edibleCollider;
    public NavMeshAgent agent;
    public WanderingAI agentAI;
    public AIWaypointPatrol patrolAI;

    [Header("Settings")]
    public Renderer jellyRenderer;

    void Awake()
    {
        //풀에서 재사용될 때 되돌릴 기준 크기.
        //흡수 연출이 크기를 줄여놓은 채로 반납되므로 복구 기준이 필요하다
        spawnScale = transform.localScale;

        _rb = GetComponent<Rigidbody>();
        edibleCollider = GetComponentInChildren<Collider>();
        jellyRenderer = GetComponentInChildren<Renderer>();
        agent = GetComponentInChildren<NavMeshAgent>();
        agentAI = GetComponentInChildren<WanderingAI>();
        patrolAI = GetComponentInChildren<AIWaypointPatrol>();

        ApplyOwnershipSetup();
    }

    // 풀에서 재사용될 때 Awake가 다시 돌지 않으므로 초기화를 따로 뽑아둔다
    private void ApplyOwnershipSetup()
    {
        if (agent != null)
            _rb.useGravity = false;
        else
            _rb.useGravity = true;

        jellyRenderer.gameObject.tag = "Edible";

        // 젤리는 주인이 없다(OwnerId=0). 호스트만 AI를 돌리고
        // 나머지는 물리를 꺼서 받은 위치와 싸우지 않게 한다.
        bool isHost = NetManager.Instance != null && NetManager.Instance.IsHost;

        if (isHost)
            return;

        if (agent != null) agent.enabled = false;
        if (_rb != null) _rb.isKinematic = true;

        // ★ AI '스크립트'는 끄지 않는다 — 껐더니 원격 화면에서
        //   젤리가 미끄러지듯 이동만 하고 걷는 애니메이션이 안 나왔다.
        //
        //   끈 이유는 "AI가 양쪽에서 돌면 안 된다"였는데, 그건 NavMeshAgent
        //   이야기다. 스크립트 자체는 소유자가 아니면 이동 로직을 건너뛰고
        //   실제 변위로 IsMoving만 맞춰주는 원격 모드로 동작한다.
        //   즉 꺼야 할 건 agent 하나뿐이고, 스크립트는 살려둬야 한다.
    }

    public void OnTakenFromPool()
    {
        StopAllCoroutines();

        absorbing = false;
        target = null;
        absorbTimer = 0f;

        //흡수 연출이 크기를 줄여놓은 채로 반납됐을 수 있다
        transform.localScale = spawnScale;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = true;

        if (edibleCollider != null)
        {
            edibleCollider.enabled = true;
            edibleCollider.isTrigger = false;
        }

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        if (agentAI != null)
            agentAI.enabled = true;

        if (patrolAI != null)
            patrolAI.enabled = true;

        ApplyOwnershipSetup();
    }

    public void OnReturnedToPool()
    {
        StopAllCoroutines();

        absorbing = false;
        target = null;

        if (agent != null && agent.enabled)
            agent.enabled = false;
    }

    // 충돌 감지에서 호출할 함수
    public void StartAbsorb(Transform player)
    {
        if (absorbing || player == null) return;

        if (patrolAI != null) patrolAI.enabled = false;
        if (agentAI != null) agentAI.enabled = false;
        if (agent != null) agent.enabled = false;

        if (_rb != null)
        {
            _rb.useGravity = false;

            //연출 동안은 transform으로 직접 몬다. 물리를 켜두면 서로 밀어내 도착 시점이 흔들린다.
            //isKinematic을 켜면 속도는 유니티가 알아서 0으로 만든다 — 직접 쓰면 경고가 난다
            _rb.isKinematic = true;
        }

        if (edibleCollider != null)
            edibleCollider.isTrigger = true;

        target = player;
        absorbing = true;
        absorbTimer = 0f;

        absorbStartPos = transform.position;
        absorbStartScale = transform.localScale;

        //거리에 비례한 연출 시간. 예전엔 0.6초 고정이라 멀리서 시작하면
        //도착 전에 시간이 끝나 젤리가 공중에서 사라졌다
        float distance = Vector3.Distance(transform.position, player.position);
        absorbDuration = Mathf.Clamp(distance / Mathf.Max(0.01f, absorbSpeed),
                                     minAbsorbTime, maxAbsorbTime);
    }

    private void Update()
    {
        if (!absorbing) return;

        //먹은 쪽이 사라지면(탈락·씬 전환) 연출을 접는다
        if (target == null)
        {
            absorbing = false;
            return;
        }

        absorbTimer += Time.deltaTime;

        float t = Mathf.Clamp01(absorbTimer / absorbDuration);

        //등속이면 밋밋하다. 처음엔 버티다 훅 빨려드는 느낌을 준다
        float k = t * t;

        //목표를 매 프레임 다시 읽어 도망가는 플레이어도 따라간다
        transform.position = Vector3.Lerp(absorbStartPos, target.position, k);
        transform.localScale = absorbStartScale * Mathf.Lerp(1f, endScaleRatio, k);

        if (t >= 1f)
            CompleteAbsorption();
    }

    /// <summary>
    /// 호스트가 흡수를 거부했을 때 젤리를 되살린다.
    ///
    /// ★ 왜 필요한가
    ///   반응성을 위해 <b>판정 전에 미리</b> 렌더러를 꺼서 먹은 것처럼 보여준다.
    ///   그런데 호스트가 거부하면(선착순에서 밀렸거나 거리 초과) 되돌릴 방법이 없어서
    ///   그 화면에만 '보이지 않는 젤리'가 남는다. 다른 사람 화면엔 멀쩡히 있으니
    ///   원인을 찾기도 어렵다.
    ///
    ///   승인됐다면 호스트가 DespawnEntity로 이 오브젝트를 없앤다.
    ///   시간이 지나도 살아 있다면 거부된 것이므로 원래대로 돌린다.
    /// </summary>
    private IEnumerator RestoreIfRejected()
    {
        yield return new WaitForSeconds(1.5f);

        // 여기까지 왔다는 건 아직 파괴되지 않았다는 뜻 = 거부됨
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;

        absorbing = false;
        target = null;
        absorbTimer = 0f;

        // 연출로 줄여둔 크기를 되돌린다. 이게 없으면 거부된 젤리가 콩알만 하게 남는다
        transform.localScale = absorbStartScale != Vector3.zero ? absorbStartScale : spawnScale;

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        if (edibleCollider != null) edibleCollider.isTrigger = false;

        if (agentAI != null) agentAI.enabled = true;
        if (patrolAI != null) patrolAI.enabled = true;

        ApplyOwnershipSetup();
    }

    // 흡수 완료 시 처리할 내용을 별도 함수로 분리
    private void CompleteAbsorption()
    {
        absorbing = false;
        OnAbsorbed();
    }

    void OnAbsorbed()
    {
        // 먹힌 즉시 로컬에서 숨겨 반응성을 유지한다(연출). 실제 파괴/보상은 호스트가 판정한다.
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // 보상(성장/색/점수)을 여기서 로컬로 즉시 지급하지 않는다.
        // 두 엔티티가 같은 젤리를 동시에 먹으면 둘 다 성장하는 이중 흡수가 되기 때문이다.
        // 호스트에게 '흡수 요청'만 보내고, 호스트가 선착 1명을 승자로 판정해 그 승자에게만 확정한다.
        // 봇 점수는 그 보상 경로(BotBridge.HandleScaleCompleted)에서 크기 기반으로 자동 산출.
        // 소켓 경로가 살아 있으면 그쪽으로 판정을 요청한다.
        //   연출은 로컬에서 즉시, 보상·파괴는 호스트가 확정한다.
        // ★ 오프라인 폴백은 없다
        //   예전엔 여기서 AbsorbMode·NetManager가 없으면 로컬에서 색을 주고
        //   Destroy(gameObject)로 끝냈다. 그런데 젤리는 NetSpawnPool이 재사용하는
        //   오브젝트라 파괴하면 풀이 깨진다. 게다가 이 게임은 반드시 로비를 거쳐
        //   들어오므로 '네트워크가 없는 판'이라는 상태 자체가 존재하지 않는다.
        //   판이 끝나 NetManager.Offline이 된 순간에 흡수 연출이 끝나는 경우만 남는데,
        //   그때는 보상을 줄 곳이 없으니 젤리를 원래대로 되돌리는 게 맞다.
        if (AbsorbMode.Instance == null || NetManager.Offline)
        {
            if (isActiveAndEnabled)
                StartCoroutine(RestoreIfRejected());
            return;
        }

        NetIdentity jellyId = GetComponent<NetIdentity>();
        NetIdentity eaterId = target != null
            ? target.GetComponentInParent<NetIdentity>() : null;

        if (jellyId == null)
        {
            Debug.LogWarning("[흡수] " + name + " 에 NetIdentity가 없습니다 — 네트워크 젤리가 아닙니다. "
                + "씬에 직접 배치된 젤리는 호스트가 스폰한 것이 아니라 동기화되지 않습니다.");
            return;
        }

        if (eaterId == null)
        {
            Debug.LogWarning("[흡수] 먹는 대상에 NetIdentity가 없습니다 (target="
                + (target != null ? target.name : "null") + ")");
            return;
        }

        AbsorbMode.Instance.RequestEat(jellyId.NetId, eaterId.NetId);

        //파괴는 호스트가 DespawnEntity로 지시한다.
        //호스트에서는 요청이 그 자리에서 처리돼 이미 풀로 돌아갔을 수 있다
        if (isActiveAndEnabled)
            StartCoroutine(RestoreIfRejected());
    }
}