using UnityEngine;
using Photon.Pun;

public class PlayerAttackState : PlayerBaseState
{
    private float _elapsed;
    private bool _hitDetected;

    private Quaternion _swingStart;
    private Quaternion _swingEnd;
    private float _swingDuration;

    public PlayerAttackState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        _elapsed = 0f;
        _hitDetected = false;

        var dm = DataManager.Instance;
        _swingDuration = dm.batSwingDuration;
        player.attackCooldownTimer = dm.batCooldown;

        float halfArc = dm.batArcAngle * 0.5f;
        _swingStart = Quaternion.Euler(0f, -halfArc, 0f);
        _swingEnd = Quaternion.Euler(0f, halfArc, 0f);

        if (player.batPivot != null)
        {
            player.batPivot.gameObject.SetActive(true);
            player.batPivot.localRotation = _swingStart;
        }

        if (player.jellyAnimator != null)
            player.jellyAnimator.SetTrigger("Attack");

        // [LAN] 원격 화면에도 공격 애니메이션이 보이도록 알린다
        JellyNet.LanPlayerVisual.ReportTrigger(player, JellyNet.LanPlayerVisual.AnimAttack);

        NetworkPlayerSync netSync = player.GetComponent<NetworkPlayerSync>();
        if (netSync != null && netSync.photonView.IsMine)
            netSync.photonView.RPC(nameof(netSync.RPC_PlayAttack), RpcTarget.Others);

        BatDebugVisualizer.NotifySwing(player.transform, dm.batRange * player.transform.localScale.x, halfArc, _swingDuration);
    }

    public override void Update()
    {
        _elapsed += Time.deltaTime;

        player.ApplyGravity();
        player.CalculateMoveDirection();
        player.MoveAndRotate();

        float t = Mathf.Clamp01(_elapsed / _swingDuration);
        if (player.batPivot != null)
            player.batPivot.localRotation = Quaternion.Slerp(_swingStart, _swingEnd, t);

        if (!_hitDetected)
            DetectBatHit();

        if (_elapsed >= _swingDuration)
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

        if (player.batPivot != null)
        {
            player.batPivot.localRotation = Quaternion.identity;
            if (player.hideBatWhenIdle)
                player.batPivot.gameObject.SetActive(false);
        }
    }

    private void DetectBatHit()
    {
        NetworkPlayerSync mySync = player.GetComponent<NetworkPlayerSync>();
        if (mySync == null || !mySync.photonView.IsMine) return;

        var dm = DataManager.Instance;
        float scale = player.transform.localScale.x;
        float range = dm.batRange * scale;
        Vector3 origin = player.transform.position + Vector3.up * (player.controller.height * 0.5f * scale);
        float halfArc = dm.batArcAngle * 0.5f;

        int mask = LayerMask.GetMask("Player") | LayerMask.GetMask("Edible");
        Collider[] hits = Physics.OverlapSphere(origin, range, mask);

        foreach (var hit in hits)
        {
            if (hit.transform.root == player.transform.root) continue;

            Vector3 toTarget = hit.transform.position - player.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.001f) continue;

            float angle = Vector3.Angle(player.transform.forward, toTarget);
            if (angle > halfArc) continue;

            NetworkPlayerSync otherPlayer = hit.GetComponentInParent<NetworkPlayerSync>();
            if (otherPlayer != null)
            {
                _hitDetected = true;
                mySync.photonView.RPC(nameof(mySync.RPC_RequestBatHitPlayer),
                    RpcTarget.MasterClient, otherPlayer.photonView.ViewID);
                return;
            }

            AIPlayerMovement aiBot = hit.GetComponentInParent<AIPlayerMovement>();
            if (aiBot != null && !aiBot.IsEliminated && !aiBot.IsBeingAbsorbed)
            {
                _hitDetected = true;
                mySync.photonView.RPC(nameof(mySync.RPC_RequestBatHitBot),
                    RpcTarget.MasterClient, aiBot.photonView.ViewID);
                return;
            }
        }
    }
}
