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
            player.Anim.SetTrigger("Dash");

        // [LAN] 원격 화면에도 대쉬 애니메이션이 보이도록 알린다
        if (player.Visual != null)
            player.Visual.SendTrigger(LanPlayerVisual.ANIM_DASH);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        player.ApplyGravity();

        // 대쉬는 순수 이동기 — 충돌/밀치기 판정 없음
        Vector3 move = dashDir * player.DashSpeed;
        move.y = player.VerticalVelocity;
        player.Controller.Move(move * Time.deltaTime);

        if (elapsed >= player.DashDuration)
        {
            if (player.IsMoveInputActive())
                player.ChangeState(player.MoveState);
            else
                player.ChangeState(player.IdleState);
        }
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.ResetTrigger("Dash");
    }
}
