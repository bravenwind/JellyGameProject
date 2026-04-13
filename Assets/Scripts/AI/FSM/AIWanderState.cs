// ============================================================
// AIWanderState.cs
// ============================================================
// 랜덤 배회 상태.
// 동기 CalculatePath + SetPath로 pathPending 문제를 회피.
// 일정 시간 정지 감지 시 강제로 새 목적지를 설정.
// ============================================================

using UnityEngine;
using UnityEngine.AI;

public class AIWanderState : AIBaseState
{
    private Vector3 _wanderTarget;
    private bool    _hasTarget    = false;
    private float   _lastSetTime  = -10f;
    private float   _stuckTimer   = 0f;

    private const float STUCK_THRESHOLD = 1.5f;

    public AIWanderState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _hasTarget  = false;
        _stuckTimer = 0f;
        ai.Agent.speed = ai.moveSpeed * 0.9f;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        // ── 정지 감지 ──
        if (ai.Agent.velocity.magnitude < 0.05f)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= STUCK_THRESHOLD)
            {
                _hasTarget = false;
                ai.Agent.ResetPath();
                _stuckTimer = 0f;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }

        // ── 새 목적지 필요 여부 판단 ──
        float distToTarget = _hasTarget
            ? Vector3.Distance(ai.transform.position, _wanderTarget) : -1f;

        bool pathFailed = _hasTarget && !ai.Agent.hasPath
                          && (Time.time - _lastSetTime) > 0.5f;
        bool pathInfinity = _hasTarget && ai.Agent.hasPath
                            && float.IsPositiveInfinity(ai.Agent.remainingDistance)
                            && (Time.time - _lastSetTime) > 0.5f;
        bool needNew = !_hasTarget || distToTarget < 1.5f || pathFailed || pathInfinity;

        if (!needNew) return;

        // ── 새 목적지 설정 ──
        if (ai.TryGetWanderDestination(out Vector3 dest))
        {
            ai.CachedPath.ClearCorners();
            if (ai.Agent.CalculatePath(dest, ai.CachedPath)
                && ai.CachedPath.status == NavMeshPathStatus.PathComplete)
            {
                _wanderTarget = dest;
                _hasTarget    = true;
                ai.Agent.SetPath(ai.CachedPath);
                _lastSetTime  = Time.time;
            }
            else
            {
                _hasTarget = false;
            }
        }
    }

    public override void Exit()
    {
        _hasTarget = false;
    }
}
