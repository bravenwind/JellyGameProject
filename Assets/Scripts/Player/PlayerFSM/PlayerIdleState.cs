using UnityEngine;
using UnityEngine.Playables;

public class PlayerIdleState : PlayerBaseState
{
    public PlayerIdleState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        player.InputDir = Vector3.zero;
    }

    public override void Update()
    {
        player.ApplyGravity();
        player.Controller.Move(new Vector3(0, player.VerticalVelocity, 0) * Time.deltaTime);

        if (player.TryStartAction())
            return;

        if (player.IsMoveInputActive())
        {
            player.ChangeState(player.MoveState);
            return;
        }
    }

    public override void Exit() { }
}
