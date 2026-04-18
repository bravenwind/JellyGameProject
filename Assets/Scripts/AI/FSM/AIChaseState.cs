using UnityEngine;
using UnityEngine.AI;

public class AIChaseState : AIBaseState
{
    private Transform _target;
    private float _pathTimer = 0f;

    private const float CHASE_PATH_RATE = 0.15f;

    public AIChaseState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _target = ai.FindTargetToChase();
        _pathTimer = CHASE_PATH_RATE;
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0f;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        // ── 타겟 유효성 체크 ──
        if (_target == null)
        {
            _target = ai.FindTargetToChase();
            if (_target == null)
            {
                // 타겟 없음 → EvaluateAndTransition이 상태 전환
                return;
            }
        }

        // ── 경로 갱신 ──
        _pathTimer += Time.deltaTime;
        if (_pathTimer >= CHASE_PATH_RATE)
        {
            _pathTimer = 0f;

            Vector3 dest = _target.position;
            if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 5f, ai.NavFilter))
                dest = hit.position;

            ai.CachedPath.ClearCorners();
            bool ok = ai.Agent.CalculatePath(dest, ai.CachedPath);

            if (ok && ai.CachedPath.status != NavMeshPathStatus.PathInvalid)
                ai.Agent.SetPath(ai.CachedPath);
        }
    }

    public override void Exit()
    {
        _target = null;
        _pathTimer = 0f;
    }
}