using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using JellyNet;

/// <summary>
/// 맵을 마구잡이로 돌아다니는 젤리.
///
/// ★ 예전엔 JellyAgentAI라는 추상 부모가 있었다
///   자식이 WanderingAI와 AIWaypointPatrol 둘이었는데, 순찰 젤리는 씬·프리팹 어디에서도
///   쓰이지 않는 죽은 코드였다. 남은 자식이 하나뿐인 상속은 "여기서 갈라진다"는
///   거짓 신호만 준다 — 어디가 공통이고 어디가 이 클래스 것인지 두 파일을 오가며 맞춰봐야 했다.
///   부모를 접어 넣어 한 파일로 만들었다.
///
///   같은 이유로 ResolveAnimator·IsReady·OnBecameDriver·DriveUpdate 네 개의
///   virtual/abstract도 사라졌다. 오버라이드할 자식이 없으면 그냥 코드다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class WanderingAI : MonoBehaviour
{
    [Header("배회 범위")]
    [SerializeField] private float wanderRadius = 10f;

    [Header("애니메이터 (비우면 자동 탐색)")]
    [SerializeField] private Animator jellyAnimController;

    private const float MOVING_SPEED = 0.1f;
    private const float SPAWN_SNAP_RADIUS = 8f;
    private const float RECOVER_SNAP_RADIUS = 5f;
    private const float DANGER_CHECK_INTERVAL = 0.5f;

    private NavMeshAgent agent;
    private Animator anim;
    private NetIdentity netId;

    private bool isMine;
    private float nextDangerCheck;

    //원격 젤리의 위치는 NetTransform이 몬다. 여기서는 애니메이션만 맞춘다
    private Vector3 lastPos;

    //경로는 매번 새로 만들지 않고 이 하나를 덮어쓴다.
    //CalculatePath는 결과를 인자에 채워넣을 뿐이라 재사용해도 안전하고,
    //배회는 계속 새 경로를 뽑으므로 프레임마다 쓰레기가 쌓이던 자리였다
    private NavMeshPath pathBuffer;

    /// <summary>
    /// 이 기계가 이 젤리를 굴리는가.
    ///
    /// IsMine이 아니라 IsSimulatedHere다 — 씬에 배치된 젤리는 OwnerId가 0이라
    /// IsMine이 어디서도 참이 아니고, 그러면 호스트조차 agent를 못 켜서 전부 얼어붙는다.
    /// 네트워크 오브젝트가 아니면(오프라인 배치 등) 그냥 로컬에서 돌린다.
    /// </summary>
    private bool IsDriver
    {
        get { return netId == null || netId.IsSimulatedHere; }
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        netId = GetComponentInParent<NetIdentity>();
        pathBuffer = new NavMeshPath();

        anim = jellyAnimController != null ? jellyAnimController : GetComponent<Animator>();
    }

    private void Start()
    {
        lastPos = transform.position;
        isMine = IsDriver;

        //남의 것이면 agent를 꺼서 수신 위치와 자체 이동이 서로 밀어내는 것을 막는다
        if (!isMine)
        {
            agent.enabled = false;
            return;
        }

        //스폰 위치가 조금이라도 어긋났으면 유니티가 agent를 꺼놓는다.
        //그대로 두면 이동 로직이 전부 isOnNavMesh에서 막혀 첫 목적지를 영영 못 받는다
        agent.enabled = true;
        SnapToNavMesh(SPAWN_SNAP_RADIUS);

        agent.avoidancePriority = Random.Range(0, 100);
        agent.updateRotation = false;   //회전은 아래에서 직접 부드럽게 돌린다

        MoveToRandomPosition();
    }

    private void Update()
    {
        if (!isMine)
        {
            //위치는 NetTransform이 몬다. 여기서는 실제 변위를 재서 걷는 애니메이션만 맞춘다
            SetMoving(NavMeshUtil.MeasureMoving(transform, ref lastPos));
            return;
        }

        //카운트다운·종료 중에는 젤리도 멈춘다. 다 같이 3·2·1 하는데 젤리만 먼저 뛰면 어색하다
        if (LanGameFlow.IsFrozen)
        {
            HoldStill();
            return;
        }

        SetMoving(agent.isOnNavMesh && agent.velocity.magnitude > MOVING_SPEED);

        //발판이 무너져 발밑 NavMesh가 carve되면 이동 로직이 전부 막힌다.
        //가까운 지점으로 되돌린다. 주변에 아예 없으면 복구를 포기하고 그대로 둔다
        if (!agent.isOnNavMesh)
        {
            SnapToNavMesh(RECOVER_SNAP_RADIUS);
            return;
        }

        SteerSmoothly();

        if (CheckDanger())
            return;

        //도착했으면 멈추지 않고 곧바로 다음 목적지를 잡는다
        if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance)
            return;

        MoveToRandomPosition();
    }

    // ─────────────────────────────────────────────────────────
    //  회전
    // ─────────────────────────────────────────────────────────
    //
    // ★ 예전엔 목적지에 닿을 때마다 1~3초 멈춰 섰다(minWaitTime/maxWaitTime)
    //   멈추는 이유가 "다음 목적지를 향해 홱 도는 것을 감추기" 였는데,
    //   서 있다가 순간적으로 방향을 바꾸는 그림이 오히려 더 눈에 띄었다.
    //   agent.updateRotation을 끄고 진행 방향으로 천천히 감아 돌게 하면
    //   멈출 이유 자체가 없어진다 — 젤리는 계속 흘러다닌다.
    private const float TURN_SPEED = 3.5f;

    private void SteerSmoothly()
    {
        Vector3 dir = agent.desiredVelocity;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f)
            return;

        transform.rotation = SmoothDamping.RotateTowards(
            transform.rotation, dir, TURN_SPEED, Time.deltaTime);
    }

    private void HoldStill()
    {
        if (agent.enabled && agent.isOnNavMesh)
        {
            if (agent.hasPath)
                agent.ResetPath();

            agent.velocity = Vector3.zero;
        }

        SetMoving(false);
    }

    private void SetMoving(bool moving)
    {
        if (anim != null)
            anim.SetBool("IsMoving", moving);
    }

    /// <summary>
    /// NavMesh 밖으로 밀려난 agent를 가까운 지점으로 되돌린다.
    /// <b>호출부가 이미 !isOnNavMesh를 확인하고 부른다</b> — 여기서 다시 보지 않는다.
    /// </summary>
    private void SnapToNavMesh(float radius)
    {
        //int 마스크 오버로드는 에이전트 타입 0(PlayerJelly) 기준이다.
        //젤리는 BearJelly 타입이라 걸어다닐 수 있는 영역이 더 좁으므로,
        //타입 0으로 찾은 자리에 Warp하면 다시 isOnNavMesh가 false가 될 수 있다
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, radius, NavMeshUtil.WalkableFilter(agent)))
            agent.Warp(hit.position);
    }

    //위험 타일 위에 있거나 위험한 곳을 향하고 있으면 즉시 새 목적지로
    private bool CheckDanger()
    {
        if (Time.time < nextDangerCheck)
            return false;

        nextDangerCheck = Time.time + DANGER_CHECK_INTERVAL;

        TileCollapseManager collapse = TileCollapseManager.Instance;
        if (collapse == null)
            return false;

        bool here = collapse.IsPositionDangerous(transform.position);
        bool ahead = agent.hasPath && collapse.IsPositionDangerous(agent.destination);

        if (!here && !ahead)
            return false;

        //안전한 무작위 목적지를 못 찾으면 위험 방향 경로를 버리고 안전지대로
        if (!MoveToRandomPosition())
        {
            agent.ResetPath();
            MoveToSafeZone(collapse);
        }

        return true;
    }

    private bool MoveToRandomPosition()
    {
        if (!TryGetRandomPointOnNavMesh(transform.position, wanderRadius, agent.agentTypeID, out Vector3 newPos))
            return false;

        return TrySetPath(newPos, checkDanger: true);
    }

    /// <summary>
    /// 무작위 목적지를 못 찾았을 때의 마지막 수단 — 아직 안 무너진 영역으로 물러난다.
    ///
    /// ★ 예전엔 안전 영역의 <b>정중앙 한 점</b>으로만 갔다
    ///   위험을 감지한 젤리가 전부 같은 좌표를 목표로 삼아서, 링이 좁아질수록
    ///   가운데 한 덩어리로 뭉쳤다. 뭉치면 서로 밀어내며 떨리고, 그 칸의 발판이
    ///   한꺼번에 마모돼 다 같이 떨어진다.
    ///   지금은 안전 영역 안의 임의 지점을 고르고, 실패하면 반경을 넓혀가며 다시 뽑는다.
    /// </summary>
    private void MoveToSafeZone(TileCollapseManager collapse)
    {
        if (!collapse.GetSafeBounds(out Vector3 min, out Vector3 max))
            return;

        NavMeshQueryFilter filter = NavMeshUtil.WalkableFilter(agent);

        for (int i = 0; i < 12; i++)
        {
            //안전 영역 안의 임의 지점. 시도가 거듭될수록 표본 반경을 넓혀 성공률을 올린다
            Vector3 candidate = new Vector3(
                Random.Range(min.x, max.x),
                (min.y + max.y) * 0.5f,
                Random.Range(min.z, max.z));

            float sampleRadius = 3f + i * 1.5f;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, filter))
                continue;

            if (collapse.IsPositionDangerous(hit.position))
                continue;

            if (TrySetPath(hit.position, checkDanger: false))
                return;
        }
    }

    /// <summary>경로를 계산해 실제로 걸 수 있으면 건다. 캐시한 pathBuffer를 덮어쓴다.</summary>
    private bool TrySetPath(Vector3 target, bool checkDanger)
    {
        if (!agent.CalculatePath(target, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete)
            return false;

        if (checkDanger)
        {
            TileCollapseManager collapse = TileCollapseManager.Instance;
            if (collapse != null && collapse.IsPathDangerous(pathBuffer.corners, pathBuffer.corners.Length))
                return false;
        }

        agent.SetPath(pathBuffer);
        return true;
    }

    //agentTypeID를 받는 이유: 이 프로젝트엔 NavMesh가 둘(PlayerJelly 0 / BearJelly)이고
    //int 마스크 오버로드는 타입 0 기준이라, 젤리가 못 가는 자리를 목적지로 잡게 된다
    public static bool TryGetRandomPointOnNavMesh(Vector3 center, float range, int agentTypeID, out Vector3 result)
    {
        TileCollapseManager collapse = TileCollapseManager.Instance;
        float sampleRadius = Mathf.Max(2f, range * 0.3f);

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeID,
            areaMask    = NavMeshUtil.WalkableMask
        };

        for (int i = 0; i < 30; i++)
        {
            Vector2 circle = Random.insideUnitCircle * range;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, filter))
                continue;

            if (collapse != null && collapse.IsPositionDangerous(hit.position))
                continue;

            result = hit.position;
            return true;
        }

        result = center;
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, wanderRadius);
    }
}
