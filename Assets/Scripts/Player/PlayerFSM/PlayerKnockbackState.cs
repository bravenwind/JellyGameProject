using UnityEngine;
using JellyNet;

public class PlayerKnockbackState : PlayerBaseState
{
    private Vector3 knockVelocity;
    private float elapsed;

    public PlayerKnockbackState(PlayerMovement player) : base(player) { }

    public void SetKnockback(Vector3 direction, float force)
    {
        knockVelocity = Knockback.StartVelocity(direction, force);
    }

    public override void Enter()
    {
        elapsed = 0f;
        if (player.Anim != null)
            player.Anim.SetTrigger(AnimParams.Hit);

        if (player.Visual != null)
            player.Visual.SendTrigger(LanPlayerVisual.ANIM_HIT);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;
        player.ApplyGravity();

        Vector3 move = Knockback.VelocityAt(knockVelocity, elapsed);
        move.y = player.VerticalVelocity;
        player.Controller.Move(move * Time.deltaTime);

        if (!Knockback.IsActive(elapsed))
        {
            player.FinishAction();
        }
    }

    public override void Exit()
    {
        knockVelocity = Vector3.zero;
        if (player.Anim != null)
            player.Anim.ResetTrigger(AnimParams.Hit);
    }
}
