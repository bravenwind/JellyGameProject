using UnityEngine;
using UnityEngine.AI;

public class AIChaseState : AIBaseState
{
    private Transform _target;
    private float _pathTimer = 0f;
    private float _reassessTimer = 0f; // 💡 주기적인 재탐색 타이머 추가
    private float _stuckTimer = 0f;

    private const float CHASE_PATH_RATE = 0.15f;
    private const float REASSESS_RATE = 0.5f;  // 💡 0.5초마다 타겟 갈아탈지 고민함

    public AIChaseState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        _target = ai.FindTargetToChase();
        _pathTimer = CHASE_PATH_RATE;
        _reassessTimer = 0f;
        _stuckTimer = 0f;
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0f;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;

        // ── 💡 주기적으로 주변을 다시 스캔해서 타겟 갱신 ──
        _reassessTimer += Time.deltaTime;
        if (_reassessTimer >= REASSESS_RATE)
        {
            _reassessTimer = 0f;
            Transform bestTarget = ai.FindTargetToChase(); // AIPlayerMovement에서 우선순위 반영된 함수 호출

            if (bestTarget != null)
            {
                _target = bestTarget; // 더 좋은 타겟(플레이어 등)이 있으면 갈아탐
            }
            else
            {
                // 타겟을 찾을 수 없다면 (모두 나보다 커졌거나 멀어졌다면) 추적 포기
                ai.EvaluateAndTransition();
                return;
            }
        }

        // 타겟이 파괴(먹힘)되었을 때의 방어 코드
        if (_target == null)
        {
            ai.EvaluateAndTransition();
            return;
        }

        // ── 💡 [추가됨] 정지(Stuck) 감지 방어 코드 ──
        // (Agent가 길을 찾고 있다고 생각하지만 실제 물리적인 속도가 안 나는 경우 강제 초기화)
        if (ai.Agent.hasPath && ai.Agent.velocity.magnitude < 0.1f)
        {
            _stuckTimer += Time.deltaTime; // 전역 변수로 private float _stuckTimer = 0f; 선언 필요
            if (_stuckTimer >= 1.0f) // 1초 이상 버벅거리면
            {
                _stuckTimer = 0f;
                ai.Agent.ResetPath(); // 현재 경로 포기 (다음 프레임에 새로 길 찾도록 유도)
                return;
            }
        }
        else
        {
            _stuckTimer = 0f;
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
            if (ai.Agent.CalculatePath(dest, ai.CachedPath) && ai.CachedPath.status == NavMeshPathStatus.PathComplete)
            {
                var collapse = TileCollapseManager.Instance;
                if (collapse != null && collapse.IsPathDangerous(ai.CachedPath.corners, ai.CachedPath.corners.Length))
                {
                    ai.EvaluateAndTransition();
                    return;
                }
                ai.Agent.SetPath(ai.CachedPath);
            }
        }
    }

    public override void Exit()
    {
        _target = null;
        _pathTimer = 0f;
    }
}