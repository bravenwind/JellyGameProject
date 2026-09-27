using UnityEngine;
using JellyNet;

public class PlayerDashState : PlayerBaseState
{
    private float elapsed;
    private Vector3 dashDir;

    public PlayerDashState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        elapsed = 0f;

        dashDir = player.InputDir.sqrMagnitude > 0.001f
            ? player.InputDir.normalized
            : player.transform.forward;

        player.StartDashCooldown();

        if (player.Anim != null)
            player.Anim.SetTrigger(AnimParams.Dash);

        if (player.Visual != null)
            player.Visual.SendTrigger(LanPlayerVisual.ANIM_DASH);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        player.ApplyGravity();

        Vector3 move = dashDir * player.DashSpeed;
        move.y = player.VerticalVelocity;
        player.Controller.Move(move * Time.deltaTime);

        if (elapsed >= player.DashDuration)
        {
            player.FinishAction();
        }
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.ResetTrigger(AnimParams.Dash);
    }
}
