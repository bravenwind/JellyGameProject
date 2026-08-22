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

    private float _retryTimer = 0f; // 목적지 탐색 실패 시 쿨다운

    public AIWanderState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _hasTarget = false;
        _retryTimer = 0f;
        ai.Agent.speed = ai.moveSpeed * 0.9f;
        ai.Agent.stoppingDistance = 0.2f; // 배회 목적지에 약간의 여유를 줌 (Jitter 방지)
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        // 재시도 대기 중이면 리턴
        if (_retryTimer > 0f)
        {
            _retryTimer -= Time.deltaTime;
            return;
        }

        // ── 새 목적지 필요 여부 판단 (기존 코드 동일) ──
        float distToTarget = _hasTarget ? Vector3.Distance(ai.transform.position, _wanderTarget) : -1f;
        bool pathFailed = _hasTarget && !ai.Agent.hasPath && (Time.time - _lastSetTime) > 0.5f;
        bool pathInfinity = _hasTarget && ai.Agent.hasPath && float.IsPositiveInfinity(ai.Agent.remainingDistance) && (Time.time - _lastSetTime) > 0.5f;
        bool needNew = !_hasTarget || distToTarget < 1.5f || pathFailed || pathInfinity;

        if (!needNew) return;

        // ── 새 목적지 설정 ──
        if (ai.TryGetWanderDestination(out Vector3 dest))
        {
            ai.CachedPath.ClearCorners();
            if (ai.Agent.CalculatePath(dest, ai.CachedPath) && ai.CachedPath.status == NavMeshPathStatus.PathComplete)
            {
                _wanderTarget = dest;
                _hasTarget = true;
                ai.Agent.SetPath(ai.CachedPath);
                _lastSetTime = Time.time;
            }
            else
            {
                _hasTarget = false;
                _retryTimer = 0.5f; // 경로 계산 실패 시 0.5초 쿨다운
            }
        }
        else
        {
            _hasTarget = false;
            _retryTimer = 0.5f; // 위치 탐색 실패 시 0.5초 쿨다운 (성능 하락 방지)
        }
    }

    public override void Exit()
    {
        _hasTarget = false;
    }
}
