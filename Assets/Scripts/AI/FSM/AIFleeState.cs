// ============================================================
// AIFleeState.cs
// ============================================================
// 나보다 큰 상대(위협)로부터 도망치는 상태.
//
// [기존 문제점 → 수정사항]
//   1. 0.4초마다 갱신 → 0.2초로 단축 (더 반응적)
//   2. 벽에 막히면 90° 오른쪽만 시도 → 8방향 시도
//   3. 이동 속도 변화 없음 → 1.5배 속도 부스트
//   4. FindThreat 조건 myLevel+1 → myLevel (1레벨 차이도 도망)
// ============================================================

using UnityEngine;
using UnityEngine.AI;

public class AIFleeState : AIBaseState
{
    private float _pathTimer = 0f;

    private const float FLEE_PATH_RATE = 0.2f;
    private const float FLEE_SPEED_MULT = 1.5f;
    private const float FLEE_DISTANCE = 15f;
    private const float MIN_VALID_DISTANCE = 5f;

    // 도망 방향 후보: 정면(0°)부터 시계/반시계 교대로 탐색
    private static readonly float[] FleeAngles =
        { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

    public AIFleeState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        ai.Agent.speed = ai.moveSpeed * FLEE_SPEED_MULT;
        _pathTimer = FLEE_PATH_RATE; // 진입 즉시 경로 계산
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        _pathTimer += Time.deltaTime;
        if (_pathTimer < FLEE_PATH_RATE) return;
        _pathTimer = 0f;

        // ── 위협 탐지 ──
        Transform threat = ai.FindThreat();
        if (threat == null) return; // 위협 사라짐 → EvaluateAndTransition이 상태 전환

        // ── 위협 반대 방향 기준으로 8방향 시도 ──
        Vector3 fleeDir = (ai.transform.position - threat.position).normalized;

        foreach (float angle in FleeAngles)
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * fleeDir;
            Vector3 candidate = ai.transform.position + dir * FLEE_DISTANCE;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 10f, ai.NavFilter))
            {
                // 벽에 막혀서 현재 위치와 거의 같은 곳이면 다음 방향 시도
                if (Vector3.Distance(ai.transform.position, hit.position) >= MIN_VALID_DISTANCE)
                {
                    ai.Agent.SetDestination(hit.position);
                    return;
                }
            }
        }

        // 모든 방향 실패 → 그냥 반대 방향으로라도 이동 시도
        Vector3 fallback = ai.transform.position + fleeDir * 5f;
        if (NavMesh.SamplePosition(fallback, out NavMeshHit fbHit, 10f, ai.NavFilter))
            ai.Agent.SetDestination(fbHit.position);
    }

    public override void Exit()
    {
        ai.Agent.speed = ai.moveSpeed; // 속도 복원
        _pathTimer = 0f;
    }
}
