using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Photon.Pun;
using Photon.Realtime;

[RequireComponent(typeof(NavMeshAgent))]
public class WanderingAI : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Wandering Settings")]
    public float wanderRadius = 10f;
    public float minWaitTime = 1f;
    public float maxWaitTime = 3f;
    public bool anchorToInitialPosition = false;

    private NavMeshAgent agent;
    private Vector3 initialPosition;
    private bool isWaiting = false;
    private float _nextDangerCheck = 0f;

    public Animator jellyAnimController;

    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    private bool _networkIsMoving;
    private bool _isMine;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        _isMine = NetworkNavMeshHelper.SetupOwnership(this, agent,
            ref _networkPosition, ref _networkRotation);

        if (_isMine)
        {
            agent.avoidancePriority = Random.Range(0, 100);
            initialPosition = transform.position;
            MoveToRandomPosition();
        }
    }

    void Update()
    {
        if (!_isMine)
        {
            NetworkNavMeshHelper.InterpolateRemote(transform, _networkPosition, _networkRotation);
            if (jellyAnimController != null)
                jellyAnimController.SetBool("IsMoving", _networkIsMoving);
            return;
        }

        bool isActuallyMoving = agent.isOnNavMesh && agent.velocity.magnitude > 0.1f;
        if (jellyAnimController != null)
            jellyAnimController.SetBool("IsMoving", isActuallyMoving);

        // NavMesh 밖에 있으면(스폰 안착 실패 / 발판 붕괴로 발 밑 NavMesh가 carve됨 등) 가장 가까운
        // NavMesh 지점으로 Warp 복구한다. 이 복구가 없으면 한 번 NavMesh를 벗어난 젤리는 바닥에 박힌
        // 채 영영 멈춰 있다(아래 이동 로직이 전부 isOnNavMesh를 전제로 하기 때문). 근처에 NavMesh가
        // 없으면 복구하지 못하므로 그대로 둔다.
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit snap, 5f, NavMesh.AllAreas))
                agent.Warp(snap.position);
            return;
        }

        // 위험 타일 위거나 위험한 곳을 향하면 즉시 새 목적지로 도망
        if (Time.time >= _nextDangerCheck)
        {
            _nextDangerCheck = Time.time + 0.5f;
            var collapse = TileCollapseManager.Instance;
            if (collapse != null)
            {
                bool curDanger = collapse.IsPositionDangerous(transform.position);
                bool destDanger = agent.hasPath && collapse.IsPositionDangerous(agent.destination);
                if (curDanger || destDanger)
                {
                    isWaiting = false;
                    // 안전한 랜덤 목적지를 못 찾으면 기존 경로(위험 방향)를 버리고 안전지대로 이동
                    if (!MoveToRandomPosition())
                    {
                        agent.ResetPath();
                        TryMoveToSafeZone(collapse);
                    }
                    return;
                }
            }
        }

        if (isWaiting) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
            {
                StartCoroutine(WaitAndMove());
            }
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        NetworkNavMeshHelper.SerializeTransform(stream, transform, agent,
            ref _networkPosition, ref _networkRotation, ref _networkIsMoving);
    }

    /// <summary>
    /// [S9] 젤리는 룸 오브젝트라 마스터가 나가도 파괴되지 않고 소유권이 새 마스터로 이전된다.
    /// 그런데 _isMine은 Start()에서 1회만 계산되므로, 새로 소유하게 된 클라가 이동 권한을 다시
    /// 잡지 않으면 NavMeshAgent가 꺼진 채 젤리가 그대로 멈춘다(봇 AIPlayerMovement는 이미 같은
    /// 방식으로 제어를 이어받는다). 여기서 소유권을 재평가해 이동을 재개한다.
    /// </summary>
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (!photonView.IsMine || _isMine) return;

        _isMine = NetworkNavMeshHelper.SetupOwnership(this, agent,
            ref _networkPosition, ref _networkRotation);

        if (_isMine && agent != null)
        {
            if (!agent.enabled) agent.enabled = true;   // 원격일 때 꺼져 있던 agent 재활성
            agent.avoidancePriority = Random.Range(0, 100);
            initialPosition = transform.position;
            // NavMesh 밖이면 Update()의 복구 로직이 가장 가까운 지점으로 Warp한다. 여기선 이동만 재개.
            MoveToRandomPosition();
        }
    }

    IEnumerator WaitAndMove()
    {
        isWaiting = true;
        float waitTime = Random.Range(minWaitTime, maxWaitTime);
        yield return new WaitForSeconds(waitTime);
        MoveToRandomPosition();
        isWaiting = false;
    }

    bool MoveToRandomPosition()
    {
        if (!agent.isOnNavMesh) return false;

        Vector3 origin = anchorToInitialPosition ? initialPosition : transform.position;
        if (TryGetRandomPointOnNavMesh(origin, wanderRadius, out Vector3 newPos))
        {
            NavMeshPath path = new NavMeshPath();
            if (agent.CalculatePath(newPos, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                var collapse = TileCollapseManager.Instance;
                if (collapse != null && collapse.IsPathDangerous(path.corners, path.corners.Length))
                    return false;
                agent.SetPath(path);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 안전한 랜덤 목적지를 못 찾았을 때, 붕괴되지 않은 안전 영역 중심으로 이동.
    /// 가장자리에서 위험을 회피하지 못해 빈 공간으로 걸어가는 것을 방지한다.
    /// </summary>
    void TryMoveToSafeZone(TileCollapseManager collapse)
    {
        if (!agent.isOnNavMesh || collapse == null) return;
        if (!collapse.GetSafeBounds(out Vector3 min, out Vector3 max)) return;

        Vector3 center = (min + max) * 0.5f;
        if (NavMesh.SamplePosition(center, out NavMeshHit hit, 15f, NavMesh.AllAreas))
        {
            NavMeshPath path = new NavMeshPath();
            if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                agent.SetPath(path);
        }
    }

    public static bool TryGetRandomPointOnNavMesh(Vector3 center, float range, out Vector3 result)
    {
        var collapse = TileCollapseManager.Instance;
        for (int i = 0; i < 30; i++)
        {
            Vector2 circle = Random.insideUnitCircle * range;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

            float sampleRadius = Mathf.Max(2f, range * 0.3f);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                if (collapse != null && collapse.IsPositionDangerous(hit.position)) continue;
                result = hit.position;
                return true;
            }
        }

        result = center;
        return false;
    }

    public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float range)
    {
        TryGetRandomPointOnNavMesh(center, range, out Vector3 result);
        return result;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = Application.isPlaying && anchorToInitialPosition ? initialPosition : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}
