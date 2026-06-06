// ============================================================
// AIFleeState.cs
// ============================================================
// 나보다 큰 상대(위협)로부터 도망치는 상태.
// 단순하게 위협의 정반대 방향으로 목적지를 설정하여 도주합니다.
// ============================================================

using UnityEngine;
using UnityEngine.AI;

public class AIFleeState : AIBaseState
{
    private float _pathTimer = 0f;
    private float _stuckTimer = 0f;

    private const float FLEE_PATH_RATE = 0.2f;
    private const float FLEE_SPEED_MULT = 1.5f;
    private const float FLEE_DISTANCE = 15f;

    public AIFleeState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        ai.Agent.speed = ai.moveSpeed * FLEE_SPEED_MULT;
        ai.Agent.stoppingDistance = 0f;
        _pathTimer = FLEE_PATH_RATE; // 진입 즉시 경로 계산
        _stuckTimer = 0f;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        // ── 💡 [추가됨] 정지(Stuck) 감지 방어 코드 ──
        // 주의: _pathTimer의 early return보다 위에 있어야 매 프레임 정상 작동합니다!
        if (ai.Agent.hasPath && ai.Agent.velocity.magnitude < 0.1f)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= 1.0f) // 1초 이상 버벅거리면
            {
                _stuckTimer = 0f;
                ai.Agent.ResetPath(); // 현재 경로 포기 (벽에 비비는 현상 탈출)
                return;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }

        // ── 경로 갱신 타이머 ──
        _pathTimer += Time.deltaTime;
        if (_pathTimer < FLEE_PATH_RATE) return; // 여기서 return 되기 전에 위에서 Stuck 체크를 마쳐야 함
        _pathTimer = 0f;

        // ── 위협 탐지 ──
        Transform threat = ai.FindThreat();

        // 💡 위협이 널이 되었다는 건 쫓아오던 애가 죽었거나, 나보다 작아졌다는 뜻!
        if (threat == null)
        {
            // 즉시 상태를 재평가해서 역으로 쫓아가거나 배회(Wander) 상태로 전환합니다.
            ai.EvaluateAndTransition();
            return;
        }

        // ── 1. Y축 단차 무시 (순수 XZ 평면 방향) ──
        Vector3 fleeDir = (ai.transform.position - threat.position);
        fleeDir.y = 0f;
        fleeDir = fleeDir.normalized;

        // ── 2. 단순 정반대 방향으로 목적지 후보 설정 ──
        Vector3 candidate = ai.transform.position + fleeDir * FLEE_DISTANCE;

        // ── 3. NavMesh 위 유효한 위치인지 확인 후 이동 ──
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 10f, ai.NavFilter)
            && TrySetSafePath(hit.position))
        {
            if (GameState.CurrentGameMode == GameModeType.Push)
                ai.TryDash();
            return; // 정방향 도주 성공
        }

        // 정방향이 위험/막힘 → 짧은 거리 폴백 → 그래도 안 되면 안전지대로
        if (!TryFallbackFlee(fleeDir))
            TryFleeToSafeZone();
    }

    /// <summary>
    /// 목적지까지 경로를 계산하고, 위험 구간을 지나지 않을 때만 SetPath.
    /// 위험하거나 경로가 불완전하면 false를 반환해 호출부가 대안을 시도하게 한다.
    /// </summary>
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

    // 💡 짧은 거리 폴백 도주 (위험 검사 포함)
    private bool TryFallbackFlee(Vector3 fleeDir)
    {
        Vector3 fallback = ai.transform.position + fleeDir * 5f;
        if (NavMesh.SamplePosition(fallback, out NavMeshHit fbHit, 5f, ai.NavFilter))
            return TrySetSafePath(fbHit.position);
        return false;
    }

    /// <summary>
    /// 모든 직선 도주 방향이 위험할 때(보통 위협이 맵 중심 쪽에 있어 도주 방향이
    /// 가장자리=붕괴 구역을 향할 때) 떨어지지 않도록 안전 영역 중심으로 도주한다.
    /// </summary>
    private void TryFleeToSafeZone()
    {
        var collapse = TileCollapseManager.Instance;
        if (collapse == null || !collapse.GetSafeBounds(out Vector3 min, out Vector3 max))
            return;

        Vector3 center = (min + max) * 0.5f;
        if (NavMesh.SamplePosition(center, out NavMeshHit hit, 15f, ai.NavFilter))
            TrySetSafePath(hit.position);
    }

    public override void Exit()
    {
        ai.Agent.speed = ai.moveSpeed; // 속도 복원
        ai.Agent.stoppingDistance = 0f; // 기본값 복원
        _pathTimer = 0f;
    }
}