using UnityEngine;
using UnityEngine.Playables;

// ==========================================
// 1. Idle 상태 클래스
// ==========================================
public class PlayerIdleState : PlayerBaseState
{
    public PlayerIdleState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        Debug.Log("[Player] 대기 상태 진입.");
        player.inputDir = Vector3.zero;
        player.verticalVelocity = 0;
    }

    public override void Update()
    {
        player.ApplyGravity();
        player.controller.Move(new Vector3(0, player.verticalVelocity, 0) * Time.deltaTime);

        if (Input.GetKeyDown(KeyCode.Space) && player.isGrounded)
        {
            player.ChangeState(player.jumpState); // 상태 변경
            return;
        }

        if (player.IsMoveInputActive())
        {
            player.ChangeState(player.moveState); // 상태 변경
            return;
        }
    }

    public override void Exit() { }
}
