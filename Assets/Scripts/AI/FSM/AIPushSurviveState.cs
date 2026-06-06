using UnityEngine;
using UnityEngine.AI;

public class AIPushSurviveState : AIBaseState
{
    private float _checkTimer;
    private bool _fleeing;

    private const float CHECK_INTERVAL = 0.15f;
    private const float FLEE_SPEED_MULT = 1.5f;

    public AIPushSurviveState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _checkTimer = 0f;
        _fleeing = false;
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0.3f;
        ai.Agent.ResetPath();
        ai.Agent.velocity = Vector3.zero;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        _checkTimer += Time.deltaTime;
        if (_checkTimer < CHECK_INTERVAL) return;
        _checkTimer = 0f;

        var collapse = TileCollapseManager.Instance;
        if (collapse == null) return;

        bool onDanger = collapse.IsPositionDangerous(ai.transform.position);

        if (onDanger)
        {
            if (!collapse.FindNearestSafeTile(ai.transform.position, out Vector3 safePos))
                return;

            ai.Agent.speed = ai.moveSpeed * FLEE_SPEED_MULT;

            if (NavMesh.SamplePosition(safePos, out NavMeshHit hit, 5f, ai.NavFilter))
            {
                ai.CachedPath.ClearCorners();
                if (ai.Agent.CalculatePath(hit.position, ai.CachedPath)
                    && ai.CachedPath.status == NavMeshPathStatus.PathComplete)
                {
                    ai.Agent.SetPath(ai.CachedPath);
                    _fleeing = true;
                }
            }
        }
        else if (_fleeing)
        {
            ai.Agent.ResetPath();
            ai.Agent.velocity = Vector3.zero;
            ai.Agent.speed = ai.moveSpeed;
            _fleeing = false;
        }
    }

    public override void Exit()
    {
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0f;
    }
}
