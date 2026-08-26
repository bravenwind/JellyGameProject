using UnityEngine;

// ==========================================
// 2. Move 상태 클래스
// ==========================================
public class PlayerMoveState : PlayerBaseState
{
    public PlayerMoveState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.Anim != null)
            player.Anim.SetBool("IsMoving", true);
        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.StartWalking();
    }

    public override void Update()
    {
        player.CalculateMoveDirection();
        player.ApplyGravity();

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

        if (!player.IsMoveInputActive())
        {
            player.ChangeState(player.IdleState);
            return;
        }

        player.MoveAndRotate();
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.SetBool("IsMoving", false);
        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.StopWalking();
    }
}
