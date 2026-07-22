using UnityEngine;
using UnityEngine.Playables;

// ==========================================
// 1. Idle(대기) 상태 클래스
// ==========================================
public class PlayerIdleState : PlayerBaseState
{
    public PlayerIdleState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        // [N1] 매 Idle 진입마다 찍히던 로그 제거 — 전환 로그는 ChangeState(에디터 전용) 한 곳으로 통일.
        player.inputDir = Vector3.zero;
        player.verticalVelocity = 0;
    }

    public override void Update()
    {
        player.ApplyGravity();
        player.controller.Move(new Vector3(0, player.verticalVelocity, 0) * Time.deltaTime);

        if (Input.GetMouseButtonDown(0) && player.CanAttack())
        {
            player.ChangeState(player.attackState);
            return;
        }

        if (Input.GetKeyDown(KeyCode.LeftShift) && player.CanDash())
        {
            player.ChangeState(player.dashState);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space) && player.isGrounded && !PlayerMovement.InputLocked)
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
