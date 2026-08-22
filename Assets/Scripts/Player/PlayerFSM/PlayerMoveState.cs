using UnityEngine;

// ==========================================
// 2. Move ���� Ŭ����
// ==========================================
public class PlayerMoveState : PlayerBaseState
{
    public PlayerMoveState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.animator != null) player.animator.SetBool("IsMoving", true);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.StartWalking();
    }

    public override void Update()
    {
        player.CalculateMoveDirection();
        player.ApplyGravity();

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

        if (!player.IsMoveInputActive())
        {
            player.ChangeState(player.idleState);
            return;
        }

        player.MoveAndRotate();
    }

    public override void Exit()
    {
        if (player.animator != null) player.animator.SetBool("IsMoving", false);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.StopWalking();
    }
}
