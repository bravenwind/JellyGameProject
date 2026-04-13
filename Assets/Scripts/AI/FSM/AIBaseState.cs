// ============================================================
// AIBaseState.cs
// ============================================================
// PlayerBaseState와 동일한 패턴의 AI 상태 기본 클래스.
// 모든 AI 상태(Wander, Chase, Flee, Scale)가 이 클래스를 상속.
// ============================================================

public abstract class AIBaseState
{
    protected AIPlayerMovement ai;

    public AIBaseState(AIPlayerMovement ai)
    {
        this.ai = ai;
    }

    /// <summary>상태 진입 시 1회 호출</summary>
    public abstract void Enter();

    /// <summary>매 프레임 호출 (AIPlayerMovement.Update에서)</summary>
    public abstract void Update();

    /// <summary>상태 이탈 시 1회 호출</summary>
    public abstract void Exit();
}
