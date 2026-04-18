// ============================================================
// AIScaleState.cs
// ============================================================
// 크기 변화(증가/감소) 중 이동을 일시 정지하는 상태.
// PlayerScaleController.IsScaling이 false가 되면 자동으로 이전 상태로 복귀.
// ============================================================

using UnityEngine;

public class AIScaleState : AIBaseState
{
    public bool IsIncreasing { get; set; } = true;

    public AIScaleState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        if (ai.Agent.isOnNavMesh)
        {
            ai.Agent.isStopped = true;
            ai.Agent.ResetPath(); // 멈추면서 기존의 무효화될 수 있는 경로 완전히 삭제
        }
    }

    public override void Update()
    {
        // 스케일 변화가 끝났는지 체크
        if (ai.ScaleCtrl == null || !ai.ScaleCtrl.IsScaling)
        {
            // 이전 상태로 복귀
            ai.RestoreStateBeforeScale();
        }
    }

    public override void Exit()
    {
        if (ai.Agent.isOnNavMesh)
            ai.Agent.isStopped = false;
    }
}
