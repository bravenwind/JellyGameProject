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
                    MoveToRandomPosition();
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

    void MoveToRandomPosition()
    {
        if (!agent.isOnNavMesh) return;

        Vector3 origin = anchorToInitialPosition ? initialPosition : transform.position;
        if (TryGetRandomPointOnNavMesh(origin, wanderRadius, out Vector3 newPos))
            agent.SetDestination(newPos);
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
