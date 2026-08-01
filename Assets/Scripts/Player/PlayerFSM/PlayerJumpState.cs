using UnityEngine;
using Photon.Pun;

// ==========================================
// 3. Jump ���� Ŭ����
// ==========================================
public class PlayerJumpState : PlayerBaseState
{
    public PlayerJumpState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        if (player.jellyAnimator != null) player.jellyAnimator.SetTrigger("Jump");

        // [LAN] 트리거는 값이 남지 않아 폴링할 수 없다 — 여기서 직접 알린다(소유자만 전송)
        JellyNet.LanPlayerVisual.ReportTrigger(player, JellyNet.LanPlayerVisual.AnimJump);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayJumpSound();

        player.verticalVelocity = player.jumpForce;

        NetworkPlayerSync networkPlayerSync = player.GetComponent<NetworkPlayerSync>();
        if (networkPlayerSync != null && networkPlayerSync.photonView.IsMine)
        {
            networkPlayerSync.photonView.RPC(nameof(networkPlayerSync.RPC_PlayJump), RpcTarget.Others);
        }
    }

    public override void Update()
    {
        player.CalculateMoveDirection();
        player.ApplyGravity();
        player.MoveAndRotate();

        if (player.verticalVelocity < 0 && player.controller.isGrounded)
        {
            player.ChangeState(player.idleState);
        }
    }

    public override void Exit()
    {
        if (player.jellyAnimator != null) player.jellyAnimator.ResetTrigger("Jump");
    }
}
