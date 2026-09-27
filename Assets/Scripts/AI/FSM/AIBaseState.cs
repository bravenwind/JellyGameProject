using UnityEngine;
using UnityEngine.AI;

public abstract class AIBaseState
{
    protected AIPlayerMovement ai;

    private float stuckTimer;

    protected const float STUCK_SECONDS = 1.0f;

    // 속도 0.1의 제곱
    private const float STUCK_SPEED_SQR = 0.01f;

    public AIBaseState(AIPlayerMovement ai)
    {
        this.ai = ai;
    }

    public abstract void Enter();

    public abstract void Update();

    public abstract void Exit();

    protected bool TrySetSafePath(Vector3 destination)
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return false;

        ai.CachedPath.ClearCorners();

        if (!ai.Agent.CalculatePath(destination, ai.CachedPath)
            || ai.CachedPath.status != NavMeshPathStatus.PathComplete)
            return false;

        var collapse = TileCollapseManager.Instance;
        if (collapse != null && collapse.IsPathDangerous(ai.CachedPath.corners))
            return false;

        ai.Agent.SetPath(ai.CachedPath);
        return true;
    }

    protected bool TrySetEscapePath(Vector3 destination, bool allowDangerousCrossing)
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return false;

        ai.CachedPath.ClearCorners();

        if (!ai.Agent.CalculatePath(destination, ai.CachedPath)
            || ai.CachedPath.status != NavMeshPathStatus.PathComplete)
            return false;

        var collapse = TileCollapseManager.Instance;
        if (collapse != null)
        {
            bool blocked = allowDangerousCrossing
                ? collapse.IsPathOverCollapsing(ai.CachedPath.corners, ai.transform.position)
                : collapse.IsPathDangerousIgnoringStart(ai.CachedPath.corners, ai.transform.position);

            if (blocked)
                return false;
        }

        ai.Agent.SetPath(ai.CachedPath);
        return true;
    }

    protected bool TrySetPartialPath(Vector3 destination)
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return false;

        ai.CachedPath.ClearCorners();

        if (!ai.Agent.CalculatePath(destination, ai.CachedPath)
            || ai.CachedPath.status == NavMeshPathStatus.PathInvalid)
            return false;

        ai.Agent.SetPath(ai.CachedPath);
        return true;
    }

    protected bool PathEndsOnFooting()
    {
        var collapse = TileCollapseManager.Instance;
        if (collapse == null)
            return true;

        Vector3[] corners = ai.CachedPath.corners;
        if (corners.Length == 0)
            return false;

        return collapse.StepsAfterArrival(corners[corners.Length - 1]) >= 0;
    }

    protected bool HandleStuck()
    {
        if (ai.Agent.hasPath && ai.Agent.velocity.sqrMagnitude < STUCK_SPEED_SQR)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= STUCK_SECONDS)
            {
                stuckTimer = 0f;
                ai.Agent.ResetPath();
                return true;
            }
            return false;
        }

        stuckTimer = 0f;
        return false;
    }

    protected void ResetStuck()
    {
        stuckTimer = 0f;
    }
}
