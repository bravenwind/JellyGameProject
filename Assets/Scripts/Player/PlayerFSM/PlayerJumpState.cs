using UnityEngine;
using JellyNet;

// ==========================================
// 3. Jump ���� Ŭ����
// ==========================================
public class PlayerJumpState : PlayerBaseState
{
    public PlayerJumpState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.animator != null) player.animator.SetTrigger("Jump");

        // [LAN] 트리거는 값이 남지 않아 폴링할 수 없다 — 여기서 직접 알린다(소유자만 전송)
        if (player.Visual != null) player.Visual.SendTrigger(LanPlayerVisual.ANIM_JUMP);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayJumpSound();

        player.verticalVelocity = player.jumpForce;
    }

    public override void Update()
    {
        player.CalculateMoveDirection();
        player.ApplyGravity();
        player.MoveAndRotate();

        if (player.verticalVelocity < 0 && player.controller.isGrounded)
        {
            player.ChangeState(player.idleState);
        }
    }

    public override void Exit()
    {
        if (player.animator != null) player.animator.ResetTrigger("Jump");
    }
}
