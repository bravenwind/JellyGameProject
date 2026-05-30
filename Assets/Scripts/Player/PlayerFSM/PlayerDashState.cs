using UnityEngine;
using Photon.Pun;

public class PlayerDashState : PlayerBaseState
{
    private float _elapsed;
    private Vector3 _dashDir;
    private bool _hitDetected;

    public PlayerDashState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        _elapsed = 0f;
        _hitDetected = false;

        _dashDir = player.inputDir.sqrMagnitude > 0.001f
            ? player.inputDir.normalized
            : player.transform.forward;

        player.dashCooldownTimer = player.dashCooldown;

        if (player.jellyAnimator != null)
            player.jellyAnimator.SetTrigger("Dash");

        NetworkPlayerSync netSync = player.GetComponent<NetworkPlayerSync>();
        if (netSync != null && netSync.photonView.IsMine)
            netSync.photonView.RPC(nameof(netSync.RPC_PlayDash), RpcTarget.Others);
    }

    public override void Update()
    {
        _elapsed += Time.deltaTime;

        player.ApplyGravity();

        Vector3 move = _dashDir * player.dashSpeed;
        move.y = player.verticalVelocity;
        player.controller.Move(move * Time.deltaTime);

        if (!_hitDetected)
            DetectDashHit();

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
        if (player.jellyAnimator != null)
            player.jellyAnimator.ResetTrigger("Dash");
    }

    private void DetectDashHit()
    {
        float radius = player.controller.radius * player.transform.localScale.x * 1.2f;
        Vector3 origin = player.transform.position + Vector3.up * (player.controller.height * 0.5f * player.transform.localScale.y);

        int playerLayer = LayerMask.GetMask("Player");
        int edibleLayer = LayerMask.GetMask("Edible");
        int mask = playerLayer | edibleLayer;

        Collider[] hits = Physics.OverlapSphere(origin, radius + 0.3f, mask);
        foreach (var hit in hits)
        {
            if (hit.transform.root == player.transform.root) continue;

            NetworkPlayerSync otherPlayer = hit.GetComponentInParent<NetworkPlayerSync>();
            if (otherPlayer != null)
            {
                _hitDetected = true;
                NetworkPlayerSync mySync = player.GetComponent<NetworkPlayerSync>();
                if (mySync != null && mySync.photonView.IsMine)
                {
                    mySync.photonView.RPC(nameof(mySync.RPC_RequestDashHitPlayer),
                        RpcTarget.MasterClient, otherPlayer.photonView.ViewID);
                }
                return;
            }

            AIPlayerMovement aiBot = hit.GetComponentInParent<AIPlayerMovement>();
            if (aiBot != null && !aiBot.IsEliminated && !aiBot.IsBeingAbsorbed)
            {
                _hitDetected = true;
                NetworkPlayerSync mySync = player.GetComponent<NetworkPlayerSync>();
                if (mySync != null && mySync.photonView.IsMine)
                {
                    mySync.photonView.RPC(nameof(mySync.RPC_RequestDashHitBot),
                        RpcTarget.MasterClient, aiBot.photonView.ViewID);
                }
                return;
            }
        }
    }
}
