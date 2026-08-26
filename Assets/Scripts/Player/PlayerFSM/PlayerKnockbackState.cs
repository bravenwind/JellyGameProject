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
            player.Anim.SetTrigger("Hit");

        // [LAN] 원격 화면에도 피격 애니메이션이 보이도록 알린다
        if (player.Visual != null)
            player.Visual.SendTrigger(LanPlayerVisual.ANIM_HIT);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;
        player.ApplyGravity();

        //속도 곡선은 봇과 공유하고, 그 속도로 어떻게 움직일지는 각자 한다.
        //사람은 CharacterController라 벽에 막힌다
        Vector3 move = Knockback.VelocityAt(knockVelocity, elapsed);
        move.y = player.VerticalVelocity;
        player.Controller.Move(move * Time.deltaTime);

        if (!Knockback.IsActive(elapsed))
        {
            if (player.IsMoveInputActive())
                player.ChangeState(player.MoveState);
            else
                player.ChangeState(player.IdleState);
        }
    }

    public override void Exit()
    {
        knockVelocity = Vector3.zero;
        if (player.Anim != null)
            player.Anim.ResetTrigger("Hit");
    }
}
