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

        //휘두르는 연출은 봇과 같은 코드를 쓴다
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
            if (player.IsMoveInputActive())
                player.ChangeState(player.MoveState);
            else
                player.ChangeState(player.IdleState);
        }
    }

    public override void Exit()
    {
        if (player.Anim != null)
            player.Anim.ResetTrigger(AnimParams.Attack);

        //배트를 원위치·숨김 처리하는 것도 BatSwingRoutine이 끝내면서 한다
    }

    /// <summary>
    /// 스윙 궤적 안의 상대를 찾아 호스트에 판정을 요청한다.
    ///
    /// ★ 판정 자체는 BatArcQuery가 한다 — 봇과 같은 코드다
    ///   예전엔 여기와 AIPlayerMovement.DetectBatHit에 같은 판정이 두 벌 있었다.
    ///   호스트의 ResolveBatHit이 거리·소유권을 다시 검사하므로,
    ///   여기서는 '누구를 때렸다고 주장하는지'만 보내면 된다.
    /// </summary>
    private void DetectBatHit()
    {
        // 히트 판정은 내 캐릭터에서만. 이 조건이 깨지면 스윙 연출만 돌고 아무도 안 맞는다
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
