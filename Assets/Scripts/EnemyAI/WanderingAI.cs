using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class WanderingAI : JellyAgentAI
{
    [Header("Wandering Settings")]
    public float wanderRadius = 10f;
    public float minWaitTime = 1f;
    public float maxWaitTime = 3f;
    public bool anchorToInitialPosition = false;

    public Animator jellyAnimController;

    private const float DANGER_CHECK_INTERVAL = 0.5f;

    private Vector3 initialPosition;
    private float nextDangerCheck;

    protected override Animator ResolveAnimator()
    {
        return jellyAnimController != null ? jellyAnimController : base.ResolveAnimator();
    }

    protected override void OnBecameDriver()
    {
        agent.avoidancePriority = Random.Range(0, 100);
        initialPosition = transform.position;

        MoveToRandomPosition();
    }

    protected override void DriveUpdate()
    {
        if (CheckDanger())
            return;

        if (isWaiting)
            return;

        if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance)
            return;

        if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
            StartCoroutine(WaitAndMove());
    }

    //위험 타일 위에 있거나 위험한 곳을 향하고 있으면 기다리던 중이라도 즉시 새 목적지로
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

        isWaiting = false;

        //안전한 무작위 목적지를 못 찾으면 위험 방향 경로를 버리고 안전지대로
        if (!MoveToRandomPosition())
        {
            agent.ResetPath();
            MoveToSafeZone(collapse);
        }

        return true;
    }

    private IEnumerator WaitAndMove()
    {
        isWaiting = true;

        yield return new WaitForSeconds(Random.Range(minWaitTime, maxWaitTime));

        MoveToRandomPosition();
        isWaiting = false;
    }

    private bool MoveToRandomPosition()
    {
        if (!agent.isOnNavMesh)
            return false;

        Vector3 origin = anchorToInitialPosition ? initialPosition : transform.position;

        if (!TryGetRandomPointOnNavMesh(origin, wanderRadius, out Vector3 newPos))
            return false;

        NavMeshPath path = new NavMeshPath();

        if (!agent.CalculatePath(newPos, path) || path.status != NavMeshPathStatus.PathComplete)
            return false;

        TileCollapseManager collapse = TileCollapseManager.Instance;

        if (collapse != null && collapse.IsPathDangerous(path.corners, path.corners.Length))
            return false;

        agent.SetPath(path);
        return true;
    }

    //가장자리에서 회피 지점을 못 찾아 빈 공간으로 걸어가는 것을 막는다
    private void MoveToSafeZone(TileCollapseManager collapse)
    {
        if (!agent.isOnNavMesh || collapse == null)
            return;

        if (!collapse.GetSafeBounds(out Vector3 min, out Vector3 max))
            return;

        Vector3 center = (min + max) * 0.5f;

        if (!NavMesh.SamplePosition(center, out NavMeshHit hit, 15f, NavMesh.AllAreas))
            return;

        NavMeshPath path = new NavMeshPath();

        if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
            agent.SetPath(path);
    }

    public static bool TryGetRandomPointOnNavMesh(Vector3 center, float range, out Vector3 result)
    {
        TileCollapseManager collapse = TileCollapseManager.Instance;
        float sampleRadius = Mathf.Max(2f, range * 0.3f);

        for (int i = 0; i < 30; i++)
        {
            Vector2 circle = Random.insideUnitCircle * range;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                continue;

            if (collapse != null && collapse.IsPositionDangerous(hit.position))
                continue;

            result = hit.position;
            return true;
        }

        result = center;
        return false;
    }

    public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float range)
    {
        TryGetRandomPointOnNavMesh(center, range, out Vector3 result);
        return result;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        Vector3 center = Application.isPlaying && anchorToInitialPosition
            ? initialPosition
            : transform.position;

        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}
