using UnityEngine;
using JellyNet;

public class PlayerJumpState : PlayerBaseState
{
    public PlayerJumpState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.Anim != null)
            player.Anim.SetTrigger(AnimParams.Jump);

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

        if (player.TryStartAction())
            return;

        if (player.VerticalVelocity < 0 && player.IsGrounded)
            player.FinishAction();
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.ResetTrigger(AnimParams.Jump);
    }
}
