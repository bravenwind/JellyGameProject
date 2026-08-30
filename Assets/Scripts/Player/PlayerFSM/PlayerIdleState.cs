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
        player.InputDir = Vector3.zero;
        player.VerticalVelocity = 0;
    }

    public override void Update()
    {
        player.ApplyGravity();
        player.Controller.Move(new Vector3(0, player.VerticalVelocity, 0) * Time.deltaTime);

        if (Input.GetMouseButtonDown(0) && player.CanAttack())
        {
            player.ChangeState(player.AttackState);
            return;
        }

        if (Input.GetKeyDown(KeyCode.LeftShift) && player.CanDash())
        {
            player.ChangeState(player.DashState);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space) && player.IsGrounded && !PlayerMovement.InputLocked)
        {
            player.ChangeState(player.JumpState);
            return;
        }

        if (player.IsMoveInputActive())
        {
            player.ChangeState(player.MoveState);
            return;
        }
    }

    public override void Exit() { }
}
