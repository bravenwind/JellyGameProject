using UnityEngine;
using UnityEngine.Playables;

// ==========================================
// 1. Idle ���� Ŭ����
// ==========================================
public class PlayerIdleState : PlayerBaseState
{
    public PlayerIdleState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        Debug.Log("[Player] ��� ���� ����.");
        player.inputDir = Vector3.zero;
        player.verticalVelocity = 0;
    }

    public override void Update()
    {
        player.ApplyGravity();
        player.controller.Move(new Vector3(0, player.verticalVelocity, 0) * Time.deltaTime);

        if (Input.GetKeyDown(KeyCode.LeftShift) && player.CanDash())
        {
            player.ChangeState(player.dashState);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space) && player.isGrounded)
        {
            player.ChangeState(player.jumpState);
            return;
        }

        if (player.IsMoveInputActive())
        {
            player.ChangeState(player.moveState);
            return;
        }
    }

    public override void Exit() { }
}
