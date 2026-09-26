using UnityEngine;

// ==========================================
// 2. Move 상태 클래스
// ==========================================
public class PlayerMoveState : PlayerBaseState
{
    public PlayerMoveState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.Anim != null)
            player.Anim.SetBool(AnimParams.IsMoving, true);
        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.StartWalking();
    }

    public override void Update()
    {
        player.CalculateMoveDirection();
        player.ApplyGravity();

        if (player.TryStartAction())
            return;

        if (!player.IsMoveInputActive())
        {
            player.ChangeState(player.IdleState);
            return;
        }

        player.MoveAndRotate();
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.SetBool(AnimParams.IsMoving, false);
        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.StopWalking();
    }
}
