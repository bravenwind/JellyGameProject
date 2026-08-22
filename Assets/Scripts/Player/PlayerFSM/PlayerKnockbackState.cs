using UnityEngine;
using JellyNet;

public class PlayerKnockbackState : PlayerBaseState
{
    private Vector3 _knockVelocity;
    private float _elapsed;

    public PlayerKnockbackState(PlayerMovement player) : base(player) { }

    public void SetKnockback(Vector3 direction, float force)
    {
        _knockVelocity = Knockback.StartVelocity(direction, force);
    }

    public override void Enter()
    {
        _elapsed = 0f;
        if (player.animator != null)
            player.animator.SetTrigger("Hit");

        // [LAN] 원격 화면에도 피격 애니메이션이 보이도록 알린다
        if (player.Visual != null) player.Visual.SendTrigger(LanPlayerVisual.ANIM_HIT);
    }

    public override void Update()
    {
        _elapsed += Time.deltaTime;
        player.ApplyGravity();

        //속도 곡선은 봇과 공유하고, 그 속도로 어떻게 움직일지는 각자 한다.
        //사람은 CharacterController라 벽에 막힌다
        Vector3 move = Knockback.VelocityAt(_knockVelocity, _elapsed);
        move.y = player.verticalVelocity;
        player.controller.Move(move * Time.deltaTime);

        if (!Knockback.IsActive(_elapsed))
        {
            if (player.IsMoveInputActive())
                player.ChangeState(player.moveState);
            else
                player.ChangeState(player.idleState);
        }
    }

    public override void Exit()
    {
        _knockVelocity = Vector3.zero;
        if (player.animator != null)
            player.animator.ResetTrigger("Hit");
    }
}
