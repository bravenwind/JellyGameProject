using UnityEngine;
using UnityEngine.AI;

public class AIPushSurviveState : AIBaseState
{
    private float _checkTimer;
    private bool _fleeing;
    private float _attackScanTimer;

    private const float CHECK_INTERVAL = 0.15f;
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

            // 속도는 플레이어와 동일(moveSpeed)하게 유지한다. 위험(무너지는 발판) 회피는
            // 속도 부스트가 아니라 아래 TryDash(짧은 대쉬 버스트)로 처리한다.
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

    // 자기보다 '작은' 먹잇감만 추격 대상으로 본다.
    // 예전엔 크기 무관 최근접 엔티티(같은 크기의 다른 봇 포함)를 쫓아서, 봇끼리 서로를 추격하며
    // 한 타일로 눈덩이처럼 뭉쳤다 → 그 타일이 step 마모로 '무너지기 직전'이 되어 다 같이 추락했다.
    // 더 큰 상대는 어차피 FindThreat→FleeState가 처리(도주)하므로, 여기서 동급/대형을 빼면
    // 봇끼리 서로 끌어당겨 뭉치는 일이 사라진다(포식자-피식자 관계만 남아 자연히 분산).
    private Transform FindNearestTarget()
    {
        float bestDist = float.MaxValue;
        Transform best = null;
        float myScale = ai.GetMyAuthorityScale();

        foreach (var player in EntityRegistry.Players)
        {
            if (player == null || player.IsOutOfPlay) continue; // 탈락/흡수 판정 단일 출처 (G6/K2)
            if (player.ScaleValue >= myScale) continue; // 나보다 크거나 같으면 추격 안 함
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
            if (bot.GetMyAuthorityScale() >= myScale) continue; // 동급/대형 봇은 추격 안 함(상호 추격 차단)
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
