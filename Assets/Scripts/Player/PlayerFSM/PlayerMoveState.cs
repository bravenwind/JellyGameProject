using UnityEngine;

// ==========================================
// 2. Move ���� Ŭ����
// ==========================================
public class PlayerMoveState : PlayerBaseState
{
    public PlayerMoveState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.jellyAnimator != null) player.jellyAnimator.SetBool("IsMoving", true);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.StartWalking();
    }

    public override void Update()
    {
        player.CalculateMoveDirection();
        player.ApplyGravity();

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

        if (!player.IsMoveInputActive())
        {
            player.ChangeState(player.idleState);
            return;
        }

        player.MoveAndRotate();
    }

    public override void Exit()
    {
        if (player.jellyAnimator != null) player.jellyAnimator.SetBool("IsMoving", false);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.StopWalking();
    }
}
