using UnityEngine;

// ==========================================
// 2. Move 상태 클래스
// ==========================================
public class PlayerMoveState : PlayerBaseState
{
    public PlayerMoveState(PlayerController player) : base(player) { }

    public override void Enter()
    {
        if (player.jellyAnimator != null) player.jellyAnimator.SetBool("IsMoving", true);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.StartWalking();
    }

    public override void Update()
    {
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

        player.CalculateMoveDirection();
        player.ApplyGravity();

        Vector3 finalMove = player.inputDir * player.moveSpeed;
        finalMove.y = player.verticalVelocity;
        player.controller.Move(finalMove * Time.deltaTime);

        if (player.inputDir != Vector3.zero)
        {
            player.targetRotation = Quaternion.LookRotation(player.inputDir);
            player.transform.rotation = Quaternion.Slerp(player.transform.rotation, player.targetRotation, player.rotateSpeed * Time.deltaTime);
        }
    }

    public override void Exit()
    {
        if (player.jellyAnimator != null) player.jellyAnimator.SetBool("IsMoving", false);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.StopWalking();
    }
}
