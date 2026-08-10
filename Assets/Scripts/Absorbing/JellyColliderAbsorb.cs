using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;

public class JellyColliderAbsorb : MonoBehaviour, JellyNet.INetPoolable
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

        // [LAN 이식] 젤리는 주인이 없다(OwnerId=0). 호스트만 AI를 돌리고
        // 나머지는 물리를 꺼서 받은 위치와 싸우지 않게 한다.
        JellyNet.NetIdentity netId = GetComponent<JellyNet.NetIdentity>();
        if (netId != null)
        {
            bool isHost = JellyNet.NetManager.Instance != null && JellyNet.NetManager.Instance.IsHost;
            if (!isHost)
            {
                // 호스트만 AI를 돌린다. 나머지는 받은 위치를 그대로 쓴다.
                // (AI가 양쪽에서 돌면 같은 젤리가 서로 다른 곳으로 간다)
                if (agent != null) agent.enabled = false;
                if (_rb != null) _rb.isKinematic = true;

                // ★ [LAN 이식] AI '스크립트'는 끄지 않는다 — 껐더니 원격 화면에서
                //   젤리가 미끄러지듯 이동만 하고 <b>걷는 애니메이션이 안 나왔다.</b>
                //
                //   끈 이유는 "AI가 양쪽에서 돌면 안 된다"였는데, 그건 NavMeshAgent
                //   이야기다. 스크립트 자체는 소유자가 아니면 이동 로직을 건너뛰고
                //   실제 변위로 IsMoving만 맞춰주는 원격 모드로 동작한다.
                //   즉 꺼야 할 건 agent 하나뿐이고, 스크립트는 살려둬야 한다.
                //   (WanderingAI.Update / AIWaypointPatrol.Update의 !_isMine 분기)
            }
        }
        else
        {
            // 기존 Photon 경로 (photon 브랜치용)
            PhotonView pv = GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine && agent != null)
            {
                agent.enabled = false;
                _rb.isKinematic = true;
            }
        }
    }

    public void OnTakenFromPool()
    {
        StopAllCoroutines();

        absorbing = false;
        target = null;
        absorbTimer = 0f;

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
    private System.Collections.IEnumerator RestoreIfRejected()
    {
        yield return new WaitForSeconds(1.5f);

        // 여기까지 왔다는 건 아직 파괴되지 않았다는 뜻 = 거부됨
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;

        absorbing = false;
        target = null;
        absorbTimer = 0f;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        if (edibleCollider != null) edibleCollider.isTrigger = false;
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
        // [LAN 이식] 소켓 경로가 살아 있으면 그쪽으로 판정을 요청한다.
        //   흐름은 Photon판과 같다: 연출은 로컬에서 즉시, 보상·파괴는 호스트가 확정.
        if (JellyNet.AbsorbMode.Instance != null
            && JellyNet.NetManager.Instance != null
            && JellyNet.NetManager.Instance.CurrentMode != JellyNet.NetManager.Mode.None)
        {
            JellyNet.NetIdentity jellyId = GetComponent<JellyNet.NetIdentity>();
            JellyNet.NetIdentity eaterId = target != null
                ? target.GetComponentInParent<JellyNet.NetIdentity>() : null;

            if (jellyId == null)
            {
                Debug.LogWarning("[흡수] " + name + " 에 NetIdentity가 없습니다 — 네트워크 젤리가 아닙니다. "
                    + "씬에 직접 배치된 젤리는 호스트가 스폰한 것이 아니라 동기화되지 않습니다.");
            }
            else if (eaterId == null)
            {
                Debug.LogWarning("[흡수] 먹는 대상에 NetIdentity가 없습니다 (target="
                    + (target != null ? target.name : "null") + ")");
            }
            else
            {
                JellyNet.AbsorbMode.Instance.RequestEat(jellyId.NetId, eaterId.NetId);

                // 호스트에서는 요청이 그 자리에서 처리돼 이미 풀로 돌아갔을 수 있다
                if (isActiveAndEnabled)
                    StartCoroutine(RestoreIfRejected());
            }

            return;   // 파괴는 호스트가 DespawnEntity로 지시한다
        }

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