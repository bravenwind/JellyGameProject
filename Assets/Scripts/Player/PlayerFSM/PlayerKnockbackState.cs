using UnityEngine;

public class PlayerKnockbackState : PlayerBaseState
{
    private Vector3 _knockVelocity;
    private float _elapsed;
    private const float KNOCKBACK_DURATION = 0.4f;

    public PlayerKnockbackState(PlayerMovement player) : base(player) { }

    public void SetKnockback(Vector3 direction, float force)
    {
        _knockVelocity = direction.normalized * force;
    }

    public override void Enter()
    {
        _elapsed = 0f;
        if (player.jellyAnimator != null)
            player.jellyAnimator.SetTrigger("Hit");
    }

    public override void Update()
    {
        _elapsed += Time.deltaTime;
        player.ApplyGravity();

        float t = _elapsed / KNOCKBACK_DURATION;
        Vector3 decayed = Vector3.Lerp(_knockVelocity, Vector3.zero, t);
        Vector3 move = decayed;
        move.y = player.verticalVelocity;
        player.controller.Move(move * Time.deltaTime);

        if (_elapsed >= KNOCKBACK_DURATION)
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
        if (player.jellyAnimator != null)
            player.jellyAnimator.ResetTrigger("Hit");
    }
}
