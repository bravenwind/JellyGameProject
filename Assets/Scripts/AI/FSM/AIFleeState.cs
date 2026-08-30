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
    private float pathTimer = 0f;

    private const float FLEE_PATH_RATE = 0.2f;
    private const float FLEE_DISTANCE = 15f;

    public AIFleeState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        // 속도는 플레이어와 동일(moveSpeed) 유지(부스트 제거). 급한 회피는 대쉬로 처리한다.
        ai.ApplyStateSpeed();
        ai.Agent.stoppingDistance = 0f;
        pathTimer = FLEE_PATH_RATE; // 진입 즉시 경로 계산
        ResetStuck();
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return;

        // ── 끼임 감지 (벽에 비비는 현상 탈출) ──
        // 주의: pathTimer의 early return보다 위에 있어야 매 프레임 누적된다
        if (HandleStuck())
            return;

        // ── 경로 갱신 타이머 ──
        pathTimer += Time.deltaTime;
        if (pathTimer < FLEE_PATH_RATE)
            return;
        pathTimer = 0f;

        // ── 위협 탐지 ──
        Transform threat = ai.Detector.FindThreat();

        // 위협이 null이 되었다는 건 쫓아오던 상대가 죽었거나 나보다 작아졌다는 뜻
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

    // 짧은 거리 폴백 도주 (TrySetSafePath가 위험 검사까지 한다)
    private bool TryFallbackFlee(Vector3 fleeDir)
    {
        Vector3 fallback = ai.transform.position + fleeDir * 5f;
        if (NavMesh.SamplePosition(fallback, out NavMeshHit fbHit, 5f, ai.NavFilter))
            return TrySetSafePath(fbHit.position);
        return false;
    }

    /// <summary>
    /// 모든 직선 도주 방향이 위험할 때(보통 위협이 맵 중심 쪽에 있어 도주 방향이
    /// 가장자리=붕괴 구역을 향할 때) 떨어지지 않도록 안전한 곳으로 도주한다.
    /// 주의: 예전엔 GetSafeBounds의 '중심 한 점'으로 보냈는데, Push 모드에선 lastShakenRing이
    /// 갱신되지 않아(=링 붕괴 미사용) 그 중심이 '맵 정중앙 고정점'이 된다. 그러면 도주하는
    /// 모든 봇이 같은 한 점에 뭉쳐 step 마모로 바닥이 동시에 무너지며 떼죽음한다.
    /// → 각 봇을 자기 위치 기준 가장 가까운 '곧 붕괴하지 않을' 타일로 분산 도주시킨다.
    /// </summary>
    private void TryFleeToSafeZone()
    {
        var collapse = TileCollapseManager.Instance;
        if (collapse == null)
            return;

        if (collapse.FindNearestSafeTile(ai.transform.position, out Vector3 safe, avoidDangerous: true))
            TrySetSafePath(safe);
    }

    public override void Exit()
    {
        ai.ApplyStateSpeed(); // 속도 복원
        ai.Agent.stoppingDistance = 0f; // 기본값 복원
        pathTimer = 0f;
    }
}