using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using JellyNet;

public class JellyColliderAbsorb : MonoBehaviour, INetPoolable
{
    // ═════════════════════════════════════════════════════════
    //  인스펙터에 내보내는 것은 '조절할 값'뿐이다
    // ═════════════════════════════════════════════════════════
    //
    // ★ 아래 것들은 [SerializeField]였지만 인스펙터 값이 한 번도 쓰이지 않았다
    //     · target / absorbing / absorbTimer — 연출 중에만 의미 있는 <b>런타임 상태</b>
    //     · edibleCollider / agent / agentAI / 렌더러 — Awake에서 GetComponent로
    //       <b>무조건 덮어쓴다.</b> 인스펙터에 뭘 꽂아도 그 한 줄에 지워졌다.
    //
    //   '고칠 수 있는 것처럼 보이는데 고쳐지지 않는 값'이 제일 헷갈린다.
    //   런타임 상태를 보고 싶으면 인스펙터를 디버그 모드로 전환하면 private도 보인다.
    private Transform target;          // 나를 먹는 쪽

    [Header("흡수 연출")]
    [Tooltip("빨려 들어가는 속도. 이동 거리를 이 값으로 나눠 연출 시간을 구한다.")]
    [SerializeField] private float absorbSpeed = 30f;

    [Tooltip("연출 시간의 하한(초). 코앞에서 먹혀도 최소 이만큼은 보여준다.")]
    [SerializeField] private float minAbsorbTime = 0.15f;

    [Tooltip("연출 시간의 상한(초). 멀리서 시작해도 이 이상 끌지 않는다.")]
    [SerializeField] private float maxAbsorbTime = 0.5f;

    [Tooltip("끝났을 때 남는 크기 비율. 작을수록 완전히 빨려 들어간 것처럼 보인다.")]
    [Range(0f, 0.3f)] [SerializeField] private float endScaleRatio = 0.05f;

    private float absorbTimer;

    private float absorbDuration = 0.3f;
    private Vector3 absorbStartPos;
    private Vector3 absorbStartScale;
    private Vector3 spawnScale;

    private Rigidbody rb;
    private bool absorbing;

    //전부 Awake에서 GetComponent로 채운다
    private Collider edibleCollider;
    private NavMeshAgent agent;
    private WanderingAI agentAI;

    // ★ 콜라이더가 하나가 아닐 수 있다
    //   *_Wandering 프리팹은 루트와 자식(Sphere005)에 각각 콜라이더가 있다.
    //   연출 중엔 <b>전부</b> 트리거로 바꿔야 한다. 대표 하나만 바꾸면 남은 콜라이더가
    //   단단한 채로 남아 빨려 들어가는 젤리가 플레이어를 밀어낸다.
    private Collider[] allColliders;

    void Awake()
    {
        //풀에서 재사용될 때 되돌릴 기준 크기.
        //흡수 연출이 크기를 줄여놓은 채로 반납되므로 복구 기준이 필요하다
        spawnScale = transform.localScale;

        rb = GetComponent<Rigidbody>();
        allColliders = GetComponentsInChildren<Collider>(true);
        edibleCollider = allColliders.Length > 0 ? allColliders[0] : null;
        agent = GetComponentInChildren<NavMeshAgent>();
        agentAI = GetComponentInChildren<WanderingAI>();

        ApplyOwnershipSetup();
    }

    /// <summary>연출 중에는 몸을 통과시키고, 끝나면 되돌린다. 콜라이더 전부에 적용한다.</summary>
    private void SetCollidersTrigger(bool asTrigger)
    {
        if (allColliders == null)
            return;

        for (int i = 0; i < allColliders.Length; i++)
        {
            if (allColliders[i] != null)
                allColliders[i].isTrigger = asTrigger;
        }
    }

    // 풀에서 재사용될 때 Awake가 다시 돌지 않으므로 초기화를 따로 뽑아둔다
    private void ApplyOwnershipSetup()
    {
        if (agent != null)
            rb.useGravity = false;
        else
            rb.useGravity = true;

        // ★ 태그는 <b>콜라이더가 있는 오브젝트</b>에 붙인다
        //   먹는 판정은 PlayerAbsorber.OnTriggerEnter(Collider other)에서
        //   other.CompareTag(Edible)로 일어난다 — 즉 콜라이더의 태그를 본다.
        //
        //   예전엔 렌더러의 오브젝트에 붙였다. 대부분의 프리팹은 둘이 같은 오브젝트라
        //   우연히 맞았지만, *_Wandering 7개는 루트에 콜라이더 · 자식에 렌더러가 있어
        //   <b>자식까지 Edible이 되어 콜라이더가 두 개</b>가 됐다.
        //   그러면 같은 젤리에 대해 OnTriggerEnter가 두 번 돈다.
        if (edibleCollider != null)
            edibleCollider.gameObject.tag = GameTags.Edible;

        // 젤리는 주인이 없다(OwnerId=0). 호스트만 AI를 돌리고
        // 나머지는 물리를 꺼서 받은 위치와 싸우지 않게 한다.
        bool isHost = NetManager.Instance != null && NetManager.Instance.IsHost;

        if (isHost)
            return;

        if (agent != null)
            agent.enabled = false;
        if (rb != null)
            rb.isKinematic = true;

        // ★ AI '스크립트'는 끄지 않는다 — 껐더니 원격 화면에서
        //   젤리가 미끄러지듯 이동만 하고 걷는 애니메이션이 안 나왔다.
        //
        //   끈 이유는 "AI가 양쪽에서 돌면 안 된다"였는데, 그건 NavMeshAgent
        //   이야기다. 스크립트 자체는 소유자가 아니면 이동 로직을 건너뛰고
        //   실제 변위로 IsMoving만 맞춰주는 원격 모드로 동작한다.
        //   즉 꺼야 할 건 agent 하나뿐이고, 스크립트는 살려둬야 한다.
    }

    /// <summary>
    /// 연출로 바꿔놓은 것을 전부 되돌려 '먹을 수 있는 젤리' 상태로 만든다.
    ///
    /// ★ 예전엔 이 코드가 두 벌이었다
    ///   OnTakenFromPool(풀에서 꺼낼 때)과 RestoreIfRejected(호스트가 거부했을 때)가
    ///   거의 같은 일을 각자 적어두고 있었다. 크기 되돌리는 기준만 서로 달랐다.
    ///   한쪽만 고치면 다른 쪽에서만 젤리가 콩알만 하게 남는다.
    /// </summary>
    /// <param name="restoreScale">되돌릴 크기. 풀에서 꺼낼 땐 스폰 크기, 거부됐을 땐 연출 시작 크기.</param>
    private void RestoreToEdible(Vector3 restoreScale)
    {
        absorbing = false;
        target = null;
        absorbTimer = 0f;

        transform.localScale = restoreScale;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = true;

        SetCollidersTrigger(false);

        if (edibleCollider != null)
            edibleCollider.enabled = true;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (agentAI != null)
            agentAI.enabled = true;

        ApplyOwnershipSetup();
    }

    public void OnTakenFromPool()
    {
        StopAllCoroutines();

        //흡수 연출이 크기를 줄여놓은 채로 반납됐을 수 있다
        RestoreToEdible(spawnScale);
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
        if (absorbing || player == null)
            return;

        if (agentAI != null)
            agentAI.enabled = false;
        if (agent != null)
            agent.enabled = false;

        if (rb != null)
        {
            rb.useGravity = false;

            //연출 동안은 transform으로 직접 몬다. 물리를 켜두면 서로 밀어내 도착 시점이 흔들린다.
            //isKinematic을 켜면 속도는 유니티가 알아서 0으로 만든다 — 직접 쓰면 경고가 난다
            rb.isKinematic = true;
        }

        //연출 중엔 콜라이더 전부를 통과시킨다. 대표 하나만 바꾸면 나머지가 단단한 채로 남는다
        SetCollidersTrigger(true);

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
        if (!absorbing)
            return;

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
    ///   승인됐다면 호스트가 DespawnEntity로 이 오브젝트를 없앤다.
    ///   시간이 지나도 살아 있다면 거부된 것이므로 원래대로 돌린다.
    ///
    /// ★ 거리 검사가 빠진 지금도 거부는 남아 있다
    ///   ResolveEat이 되돌려보내는 경우는 넷이다 — 판이 이미 끝났을 때(Phase != Playing),
    ///   젤리를 못 찾을 때, 연출이 도는 동안 먹는이가 탈락했을 때, 소유권이 안 맞을 때.
    ///   앞의 둘·셋은 정상 플레이에서도 난다(종료 직전에 먹으면 그렇다).
    ///
    ///   선착순에서 밀린 경우는 여기가 아니라 디스폰이 처리한다 —
    ///   젤리가 풀로 돌아가면서 OnReturnedToPool의 StopAllCoroutines가 이 코루틴을 끊고,
    ///   다음에 꺼낼 때 OnTakenFromPool이 같은 복구를 한다.
    /// </summary>
    private IEnumerator RestoreIfRejected()
    {
        yield return new WaitForSeconds(1.5f);

        // 여기까지 왔다는 건 아직 파괴되지 않았다는 뜻 = 거부됨
        RestoreToEdible(absorbStartScale != Vector3.zero ? absorbStartScale : spawnScale);
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