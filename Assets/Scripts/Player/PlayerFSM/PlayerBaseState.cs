using UnityEngine;

public abstract class PlayerBaseState
{
    protected PlayerMovement player;

    public PlayerBaseState(PlayerMovement player)
    {
        this.player = player;
    }

    public abstract void Enter();
    public abstract void Update();
    public abstract void Exit();
}