using UnityEngine;
using JellyNet;

public class PlayerAttackState : PlayerBaseState
{
    private float elapsed;
    private bool hitDetected;

    private float swingDuration;

    public PlayerAttackState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        elapsed = 0f;
        hitDetected = false;

        DataManager dm = DataManager.Instance;
        swingDuration = dm != null ? dm.BatSwingDuration : 0f;

        player.StartAttackCooldown();

        BatSwing.Play(player.transform, player.Anim, player.Visual, player.AuthorityScale);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        player.ApplyGravity();
        player.CalculateMoveDirection();
        player.MoveAndRotate();

        if (!hitDetected)
            DetectBatHit();

        if (elapsed >= swingDuration)
        {
            player.FinishAction();
        }
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.ResetTrigger(AnimParams.Attack);
    }

    private void DetectBatHit()
    {
        NetIdentity myId = player.GetComponentInParent<NetIdentity>();

        if (myId == null || !myId.IsMine)
            return;

        PushMode push = PushMode.Instance;

        if (push == null)
            return;

        NetIdentity victim = BatArcQuery.Find(player.transform, myId, player.AuthorityScale);

        if (victim == null)
            return;

        hitDetected = true;
        push.RequestBatHit(victim.NetId, myId.NetId);
    }
}
