using UnityEngine;
using JellyNet;

// ==========================================
// 3. Jump 상태 클래스
// ==========================================
public class PlayerJumpState : PlayerBaseState
{
    public PlayerJumpState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.Anim != null)
            player.Anim.SetTrigger(AnimParams.Jump);

        // [LAN] 트리거는 값이 남지 않아 폴링할 수 없다 — 여기서 직접 알린다(소유자만 전송)
        if (player.Visual != null)
            player.Visual.SendTrigger(LanPlayerVisual.ANIM_JUMP);
        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.PlayJumpSound();

        player.VerticalVelocity = player.JumpForce;
    }

    public override void Update()
    {
        player.CalculateMoveDirection();
        player.ApplyGravity();
        player.MoveAndRotate();

        if (player.VerticalVelocity < 0 && player.Controller.isGrounded)
            player.ChangeState(player.IdleState);
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.ResetTrigger(AnimParams.Jump);
    }
}
