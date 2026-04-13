// ============================================================
// AIChaseState.cs
// ============================================================
// 가장 가까운 젤리를 추적하는 상태.
// CHASE_PATH_RATE 간격으로 경로를 갱신하며,
// 대상이 사라지면 EvaluateAndTransition을 통해 다른 상태로 전환.
// ============================================================

using UnityEngine;
using UnityEngine.AI;

public class AIChaseState : AIBaseState
{
    private Transform _target;
    private float     _pathTimer = 0f;

    private const float CHASE_PATH_RATE = 0.15f;

    public AIChaseState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _target    = ai.FindNearestJelly();
        _pathTimer = CHASE_PATH_RATE; // 진입 즉시 경로 계산
        ai.Agent.speed = ai.moveSpeed;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        // ── 타겟 유효성 체크 ──
        if (_target == null)
        {
            _target = ai.FindNearestJelly();
            if (_target == null)
            {
                // 젤리 없음 → 상태 전환은 EvaluateAndTransition이 처리
                return;
            }
        }

        // ── 경로 갱신 ──
        _pathTimer += Time.deltaTime;
        if (_pathTimer >= CHASE_PATH_RATE)
        {
            _pathTimer = 0f;

            // 젤리는 NavMesh 위에 없을 수 있으므로 스냅
            Vector3 dest = _target.position;
            if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                dest = hit.position;

            ai.CachedPath.ClearCorners();
            bool ok = ai.Agent.CalculatePath(dest, ai.CachedPath);

            if (ok && ai.CachedPath.status != NavMeshPathStatus.PathInvalid)
                ai.Agent.SetPath(ai.CachedPath);
        }
    }

    public override void Exit()
    {
        _target    = null;
        _pathTimer = 0f;
    }
}
