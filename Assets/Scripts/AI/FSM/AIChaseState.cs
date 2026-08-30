using UnityEngine;
using UnityEngine.AI;

public class AIChaseState : AIBaseState
{
    private Transform target;
    private float pathTimer = 0f;
    private float reassessTimer = 0f;

    private const float CHASE_PATH_RATE = 0.15f;
    private const float REASSESS_RATE = 0.5f;  // 0.5초마다 더 나은 타겟이 있는지 다시 본다

    public AIChaseState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        target = ai.Detector.FindTargetToChase();
        pathTimer = CHASE_PATH_RATE;   //진입 즉시 경로 계산
        reassessTimer = 0f;
        ResetStuck();
        ai.ApplyStateSpeed();
        ai.Agent.stoppingDistance = 0f;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return;

        // ── 주기적으로 주변을 다시 스캔해서 타겟 갱신 ──
        reassessTimer += Time.deltaTime;
        if (reassessTimer >= REASSESS_RATE)
        {
            reassessTimer = 0f;
            Transform bestTarget = ai.Detector.FindTargetToChase();

            //더 좋은 타겟(가까운 먹잇감 등)이 있으면 갈아탄다.
            //아무도 없으면(전부 나보다 커졌거나 멀어졌으면) 추격 자체를 접는다
            if (bestTarget == null)
            {
                ai.EvaluateAndTransition();
                return;
            }
            target = bestTarget;
        }

        // 타겟이 파괴(먹힘)되었을 때의 방어 코드
        if (target == null)
        {
            ai.EvaluateAndTransition();
            return;
        }

        // ── 끼임 감지 (경로는 있는데 안 움직임) ──
        if (HandleStuck())
            return;

        // ── 경로 갱신 ──
        pathTimer += Time.deltaTime;
        if (pathTimer < CHASE_PATH_RATE)
            return;
        pathTimer = 0f;

        Vector3 dest = target.position;
        if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 5f, ai.NavFilter))
            dest = hit.position;

        //실패(경로 불완전 / 위험 구간 경유)하면 이번 주기만 건너뛴다.
        //예전엔 여기서 EvaluateAndTransition()을 불렀는데, 먹잇감이 그대로 있으니
        //대개 같은 ChaseState로 되돌아오는 no-op이었다(ChangeState가 동일 상태를 막는다).
        //얻는 것 없이 위협·타겟 스캔만 두 번 더 돌렸고, 상태 재평가는 어차피
        //StateEvalLoop이 같은 주기(0.15초)로 하고 있다.
        TrySetSafePath(dest);
    }

    public override void Exit()
    {
        target = null;
        pathTimer = 0f;
    }
}
