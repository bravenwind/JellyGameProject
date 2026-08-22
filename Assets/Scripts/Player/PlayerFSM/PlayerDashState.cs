using UnityEngine;
using JellyNet;

public class PlayerDashState : PlayerBaseState
{
    private float _elapsed;
    private Vector3 _dashDir;

    public PlayerDashState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        _elapsed = 0f;

        _dashDir = player.inputDir.sqrMagnitude > 0.001f
            ? player.inputDir.normalized
            : player.transform.forward;

        player.dashCooldownTimer = player.dashCooldown;

        if (player.animator != null)
            player.animator.SetTrigger("Dash");

        // [LAN] 원격 화면에도 대쉬 애니메이션이 보이도록 알린다
        if (player.Visual != null) player.Visual.SendTrigger(LanPlayerVisual.ANIM_DASH);
    }

    public override void Update()
    {
        _elapsed += Time.deltaTime;

        player.ApplyGravity();

        // 대쉬는 순수 이동기 — 충돌/밀치기 판정 없음
        Vector3 move = _dashDir * player.dashSpeed;
        move.y = player.verticalVelocity;
        player.controller.Move(move * Time.deltaTime);

        if (_elapsed >= player.dashDuration)
        {
            if (player.IsMoveInputActive())
                player.ChangeState(player.moveState);
            else
                player.ChangeState(player.idleState);
        }
    }

    public override void Exit()
    {
        if (player.animator != null)
            player.animator.ResetTrigger("Dash");
    }
}
