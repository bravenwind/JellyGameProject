using UnityEngine;

// 상태 정의
public enum UnitState
{
    Idle,
    Move,
    Patrol,
    Jump
}

public abstract class BaseFSM : MonoBehaviour
{
    // 현재 상태 (외부에서 함부로 못 바꾸게 protected/private set)
    [SerializeField]
    protected UnitState currentState = UnitState.Idle;

    protected virtual void Start()
    {
        // 시작 시 초기 상태 진입 로직 실행
        OnEnterState(currentState);
    }

    protected virtual void Update()
    {
        OnUpdateState(currentState);
    }

    protected virtual void FixedUpdate()
    {
        // 물리 관련만
    }


    // 핵심 1: 상태 변경 함수
    // 외부나 내부에서 이 함수를 통해 상태를 바꿈
    public void ChangeState(UnitState newState)
    {
        if (currentState == newState) return;

        // 1. 이전 상태 종료 (Exit) - 1회 실행
        OnExitState(currentState);

        // 상태 교체
        UnitState previousState = currentState;
        currentState = newState;

        // 2. 새로운 상태 진입 (Enter) - 1회 실행
        // 여기서 로그를 찍거나 스위치를 보면 흐름이 직관적으로 보임
        OnEnterState(currentState);

        Debug.Log($"[FSM] State Changed: {previousState} -> {currentState}");
    }

    // ---------------------------------------------------------
    // 아래 함수들은 자식 클래스가 구현하거나 오버라이드 할 영역들
    // ---------------------------------------------------------

    // 상태 진입 시 1회 실행되는 로직 (스위치로 분기)
    protected virtual void OnEnterState(UnitState state)
    {
        switch (state)
        {
            case UnitState.Idle: Enter_Idle(); break;
            case UnitState.Move: Enter_Move(); break;
            case UnitState.Patrol: Enter_Patrol(); break;
            case UnitState.Jump: Enter_Jump(); break;
        }
    }

    // 상태 종료 시 1회 실행되는 로직
    protected virtual void OnExitState(UnitState state)
    {
        switch (state)
        {
            case UnitState.Idle: Exit_Idle(); break;
            case UnitState.Move: Exit_Move(); break;
            case UnitState.Patrol: Exit_Patrol(); break;
            case UnitState.Jump: Exit_Jump(); break;
        }
    }

    // 매 프레임 반복 실행되는 로직
    protected virtual void OnUpdateState(UnitState state)
    {
        switch (state)
        {
            case UnitState.Idle: Update_Idle(); break;
            case UnitState.Move: Update_Move(); break;
            case UnitState.Patrol: Update_Patrol(); break;
            case UnitState.Jump: Update_Jump(); break;
        }
    }

    // 구체적인 행동 정의 (빈 가상 함수로 두어 자식이 구현 강제 혹은 선택)
    // "행동 단위 정의를 사용자가 오버라이드" 할 수 있는 부분
    protected virtual void Enter_Idle() { }
    protected virtual void Update_Idle() { }
    protected virtual void Exit_Idle() { }

    protected virtual void Enter_Move() { }
    protected virtual void Update_Move() { }
    protected virtual void Exit_Move() { }

    protected virtual void Enter_Patrol() { }
    protected virtual void Update_Patrol() { }
    protected virtual void Exit_Patrol() { }

    protected virtual void Enter_Jump() { }
    protected virtual void Update_Jump() { }
    protected virtual void Exit_Jump() { }
}