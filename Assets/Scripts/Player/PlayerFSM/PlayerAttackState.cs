using UnityEngine;
using Photon.Pun;

public class PlayerAttackState : PlayerBaseState
{
    private float _elapsed;
    private bool _hitDetected;

    public PlayerAttackState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        _elapsed = 0f;
        _hitDetected = false;

        player.attackCooldownTimer = DataManager.Instance.batCooldown;

        if (player.jellyAnimator != null)
            player.jellyAnimator.SetTrigger("Attack");

        NetworkPlayerSync netSync = player.GetComponent<NetworkPlayerSync>();
        if (netSync != null && netSync.photonView.IsMine)
            netSync.photonView.RPC(nameof(netSync.RPC_PlayAttack), RpcTarget.Others);
    }

    public override void Update()
    {
        _elapsed += Time.deltaTime;

        // 이동하면서 공격 가능
        player.ApplyGravity();
        player.CalculateMoveDirection();
        player.MoveAndRotate();

        if (!_hitDetected)
            DetectBatHit();

        if (_elapsed >= DataManager.Instance.batSwingDuration)
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
            player.jellyAnimator.ResetTrigger("Attack");
    }

    private void DetectBatHit()
    {
        var dm = DataManager.Instance;
        float scale = player.transform.localScale.x;
        float range = dm.batRange * scale;
        Vector3 origin = player.transform.position + Vector3.up * (player.controller.height * 0.5f * scale);
        float halfArc = dm.batArcAngle * 0.5f;

        int playerLayer = LayerMask.GetMask("Player");
        int edibleLayer = LayerMask.GetMask("Edible");
        int mask = playerLayer | edibleLayer;

        Collider[] hits = Physics.OverlapSphere(origin, range, mask);
        foreach (var hit in hits)
        {
            if (hit.transform.root == player.transform.root) continue;

            Vector3 toTarget = (hit.transform.position - player.transform.position);
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.001f) continue;

            float angle = Vector3.Angle(player.transform.forward, toTarget);
            if (angle > halfArc) continue;

            NetworkPlayerSync otherPlayer = hit.GetComponentInParent<NetworkPlayerSync>();
            if (otherPlayer != null)
            {
                _hitDetected = true;
                NetworkPlayerSync mySync = player.GetComponent<NetworkPlayerSync>();
                if (mySync != null && mySync.photonView.IsMine)
                {
                    mySync.photonView.RPC(nameof(mySync.RPC_RequestBatHitPlayer),
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
                    mySync.photonView.RPC(nameof(mySync.RPC_RequestBatHitBot),
                        RpcTarget.MasterClient, aiBot.photonView.ViewID);
                }
                return;
            }
        }
    }
}
