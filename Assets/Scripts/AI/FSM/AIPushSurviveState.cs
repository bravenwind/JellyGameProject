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
        if (ai.IsDashing || ai.IsAttacking) return;

        _checkTimer += Time.deltaTime;
        if (_checkTimer < CHECK_INTERVAL) return;
        _checkTimer = 0f;

        var collapse = TileCollapseManager.Instance;
        if (collapse == null) return;

        bool onDanger = collapse.IsPositionDangerous(ai.transform.position);

        if (onDanger)
        {
            // avoidDangerous: 마모가 한계 직전인 타일은 도피처에서 제외 → 닳은 타일로 몰려가
            // 서로의 step 마모를 가속하다 한곳에서 동시 붕괴하는 현상을 막는다.
            if (!collapse.FindNearestSafeTile(ai.transform.position, out Vector3 safePos, avoidDangerous: true))
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
            UpdateCombatOrWander();
        }
    }

    /// <summary>
    /// 안전한 상태일 때의 행동. 근처에 타겟이 있으면 추격/공격, 없으면 위험 타일을
    /// 피해 평소처럼 배회(roam)한다.
    /// </summary>
    private void UpdateCombatOrWander()
    {
        _attackScanTimer += CHECK_INTERVAL;
        if (_attackScanTimer < ATTACK_SCAN_INTERVAL) return;
        _attackScanTimer = 0f;

        Transform target = FindNearestTarget();
        if (target != null && TryEngageTarget(target))
            return;

        // 타겟이 없거나 추격 범위 밖 → 평소엔 배회
        Wander();
    }

    /// <summary>타겟을 추격하거나 사거리 안이면 공격. 무언가 행동했으면 true.</summary>
    private bool TryEngageTarget(Transform target)
    {
        Vector3 dirToTarget = target.position - ai.transform.position;
        dirToTarget.y = 0;
        float dist = dirToTarget.magnitude;

        var dm = DataManager.Instance;
        float range = dm != null ? dm.batRange * ai.transform.localScale.x : 2f;

        if (dist <= range * 1.2f)
        {
            FaceTarget(dirToTarget);
            ai.TryAttack();
            return true;
        }

        if (dist < ai.detectRadius * 0.6f
            && NavMesh.SamplePosition(target.position, out NavMeshHit hit, 5f, ai.NavFilter))
        {
            ai.Agent.speed = ai.moveSpeed;
            if (TrySetSafePath(hit.position))
                return true;
        }

        return false;
    }

    /// <summary>위험 타일을 피해 무작위 안전 지점으로 배회한다.</summary>
    private void Wander()
    {
        // 이미 경로를 따라 이동 중이면 목적지 도착/경로 소실 전까진 그대로 둔다.
        if (ai.Agent.pathPending) return;
        if (ai.Agent.hasPath && ai.Agent.remainingDistance > ai.Agent.stoppingDistance + 0.5f)
            return;

        ai.Agent.speed = ai.moveSpeed;

        if (ai.TryGetWanderDestination(out Vector3 dest)
            && NavMesh.SamplePosition(dest, out NavMeshHit hit, 5f, ai.NavFilter))
        {
            TrySetSafePath(hit.position);
        }
    }

    /// <summary>위험 구간을 지나지 않는 경로일 때만 SetPath. (붕괴 타일 회피 이중 안전장치)</summary>
    private bool TrySetSafePath(Vector3 destination)
    {
        ai.CachedPath.ClearCorners();
        if (!ai.Agent.CalculatePath(destination, ai.CachedPath)
            || ai.CachedPath.status != NavMeshPathStatus.PathComplete)
            return false;

        var collapse = TileCollapseManager.Instance;
        if (collapse != null && collapse.IsPathDangerous(ai.CachedPath.corners, ai.CachedPath.corners.Length))
            return false;

        ai.Agent.SetPath(ai.CachedPath);
        return true;
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
