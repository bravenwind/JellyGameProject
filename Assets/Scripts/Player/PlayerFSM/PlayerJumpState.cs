using UnityEngine;

// ==========================================
// 3. Jump 상태 클래스
// ==========================================
public class PlayerJumpState : PlayerBaseState
{
    public PlayerJumpState(PlayerController player) : base(player) { }

    public override void Enter()
    {
        if (player.jellyAnimator != null) player.jellyAnimator.SetTrigger("Jump");
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayJumpSound();

        player.verticalVelocity = player.jumpForce;
    }

    public override void Update()
    {
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

        if (player.verticalVelocity < 0 && player.controller.isGrounded)
        {
            player.ChangeState(player.idleState);
        }
    }

    public override void Exit() { }
}
