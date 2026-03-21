using UnityEngine;

// 상태 정의
public enum UnitState
{
    Idle,
    Move,
    Patrol,
    Jump
}

public abstract class PlayerBaseState
{
    protected PlayerController player;

    public PlayerBaseState(PlayerController player)
    {
        this.player = player;
    }

    public abstract void Enter();
    public abstract void Update();
    public abstract void Exit();
}