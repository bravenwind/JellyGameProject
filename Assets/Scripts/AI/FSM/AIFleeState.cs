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

    private const float FLEE_PATH_RATE = 0.2f;
    private const float FLEE_SPEED_MULT = 1.5f;
    private const float FLEE_DISTANCE = 15f;

    public AIFleeState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        ai.Agent.speed = ai.moveSpeed * FLEE_SPEED_MULT;
        ai.Agent.stoppingDistance = 0f;
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
        // 탐색 반경(10f) 내에 갈 수 있는 땅이 있다면 그곳으로 뜁니다.
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 10f, ai.NavFilter))
        {
            ai.Agent.SetDestination(hit.position);
        }
        else
        {
            // 만약 맵의 완전 끝자락이라 15유닛 바깥에 땅이 아예 없다면, 
            // 조금 덜 멀리(5유닛) 떨어진 곳이라도 찾아서 비비도록 처리
            Vector3 fallback = ai.transform.position + fleeDir * 5f;
            if (NavMesh.SamplePosition(fallback, out NavMeshHit fbHit, 5f, ai.NavFilter))
            {
                ai.Agent.SetDestination(fbHit.position);
            }
        }
    }

    public override void Exit()
    {
        ai.Agent.speed = ai.moveSpeed; // 속도 복원
        ai.Agent.stoppingDistance = 0f; // 기본값 복원
        _pathTimer = 0f;
    }
}