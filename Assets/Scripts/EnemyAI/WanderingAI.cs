using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Photon.Pun;

[RequireComponent(typeof(NavMeshAgent))]
public class WanderingAI : MonoBehaviourPun, IPunObservable
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

        if (!agent.isOnNavMesh) return;

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
