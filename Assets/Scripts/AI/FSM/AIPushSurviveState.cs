using UnityEngine;
using UnityEngine.AI;

public class AIPushSurviveState : AIBaseState
{
    private float _checkTimer;
    private bool _fleeing;
    private float _attackScanTimer;

    private const float CHECK_INTERVAL = 0.15f;
    private const float FLEE_SPEED_MULT = 1.5f;
    private const float ATTACK_SCAN_INTERVAL = 0.3f;

    public AIPushSurviveState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _checkTimer = 0f;
        _fleeing = false;
        _attackScanTimer = 0f;
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0.3f;
        ai.Agent.ResetPath();
        ai.Agent.velocity = Vector3.zero;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;
        if (ai.IsDashing) return;

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
                    ai.TryDash();
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
        else
        {
            ScanAndAttack();
        }
    }

    private void ScanAndAttack()
    {
        _attackScanTimer += CHECK_INTERVAL;
        if (_attackScanTimer < ATTACK_SCAN_INTERVAL) return;
        _attackScanTimer = 0f;

        Transform target = FindNearestTarget();
        if (target == null) return;

        Vector3 dirToTarget = target.position - ai.transform.position;
        dirToTarget.y = 0;
        float dist = dirToTarget.magnitude;

        var dm = DataManager.Instance;
        float range = dm != null ? dm.batRange * ai.transform.localScale.x : 2f;

        if (dist <= range * 1.2f)
        {
            FaceTarget(dirToTarget);
            ai.TryAttack();
        }
        else if (dist < ai.detectRadius * 0.6f)
        {
            if (NavMesh.SamplePosition(target.position, out NavMeshHit hit, 5f, ai.NavFilter))
            {
                ai.CachedPath.ClearCorners();
                if (ai.Agent.CalculatePath(hit.position, ai.CachedPath)
                    && ai.CachedPath.status == NavMeshPathStatus.PathComplete)
                {
                    ai.Agent.speed = ai.moveSpeed;
                    ai.Agent.SetPath(ai.CachedPath);
                }
            }
        }
    }

    private Transform FindNearestTarget()
    {
        float bestDist = float.MaxValue;
        Transform best = null;

        foreach (var player in EntityRegistry.Players)
        {
            if (player == null) continue;
            if (player.photonView.Owner?.CustomProperties != null &&
                player.photonView.Owner.CustomProperties.TryGetValue("Eliminated", out object e) &&
                e is bool b && b) continue;
            float d = Vector3.Distance(ai.transform.position, player.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = player.transform;
            }
        }

        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || bot == ai || bot.IsEliminated || bot.IsBeingAbsorbed) continue;
            float d = Vector3.Distance(ai.transform.position, bot.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = bot.transform;
            }
        }

        return bestDist < ai.detectRadius ? best : null;
    }

    private void FaceTarget(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.01f) return;
        ai.transform.rotation = Quaternion.LookRotation(direction.normalized);
    }

    public override void Exit()
    {
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0f;
    }
}
