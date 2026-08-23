// ============================================================
// AIWanderState.cs
// ============================================================
// 랜덤 배회 상태.
// 동기 CalculatePath + SetPath로 pathPending 문제를 회피.
// 일정 시간 정지 감지 시 강제로 새 목적지를 설정.
// ============================================================

using UnityEngine;

public class AIWanderState : AIBaseState
{
    private Vector3 _wanderTarget;
    private bool    _hasTarget    = false;
    private float   _lastSetTime  = -10f;

    private float _retryTimer = 0f; // 목적지 탐색 실패 시 쿨다운

    //목적지에 완전히 도착하기 전에 다음 목적지를 잡는 거리.
    //도착까지 기다리면 감속 → 정지 → 재탐색이 되어 배회가 뚝뚝 끊긴다
    private const float REPLAN_DISTANCE = 1.5f;

    private const float RETRY_COOLDOWN = 0.5f;

    //SetPath 직후 한두 프레임은 hasPath/remainingDistance가 안정되지 않는다
    private const float PATH_SETTLE = 0.5f;

    public AIWanderState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _hasTarget = false;
        _retryTimer = 0f;
        ResetStuck();
        ai.Agent.speed = ai.moveSpeed * 0.9f;
        ai.Agent.stoppingDistance = 0.2f; // 배회 목적지에 약간의 여유를 줌 (Jitter 방지)
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        //경로는 있는데 안 움직이면 버리고 다시 잡는다
        if (HandleStuck())
        {
            _hasTarget = false;
            return;
        }

        // 재시도 대기 중이면 리턴
        if (_retryTimer > 0f)
        {
            _retryTimer -= Time.deltaTime;
            return;
        }

        // ── 새 목적지 필요 여부 판단 ──
        float distToTarget = _hasTarget ? Vector3.Distance(ai.transform.position, _wanderTarget) : -1f;
        bool settled = _hasTarget && (Time.time - _lastSetTime) > PATH_SETTLE;

        //경로가 통째로 사라졌다
        bool pathFailed = settled && !ai.Agent.hasPath;

        //경로는 있는데 남은 거리가 무한 = 붕괴 타일의 NavMeshObstacle이 길을 끊었다
        bool pathInfinity = settled && ai.Agent.hasPath
                            && float.IsPositiveInfinity(ai.Agent.remainingDistance);

        bool needNew = !_hasTarget || distToTarget < REPLAN_DISTANCE || pathFailed || pathInfinity;

        if (!needNew) return;

        // ── 새 목적지 설정 ──
        //TrySetSafePath가 완주 가능 + 위험 구간 회피까지 확인한다.
        //예전엔 여기서 CalculatePath/SetPath를 직접 불러 status만 봤고 위험 검사가 빠져 있어서,
        //목적지만 안전하면 무너지는 링을 관통하는 경로도 그대로 탔다
        if (ai.TryGetWanderDestination(out Vector3 dest) && TrySetSafePath(dest))
        {
            _wanderTarget = dest;
            _hasTarget = true;
            _lastSetTime = Time.time;
            return;
        }

        _hasTarget = false;
        _retryTimer = RETRY_COOLDOWN; // 실패 시 쿨다운 (매 프레임 재시도로 인한 성능 하락 방지)
    }

    public override void Exit()
    {
        _hasTarget = false;
    }
}
