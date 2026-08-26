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

        var dm = DataManager.Instance;
        swingDuration = dm.BatSwingDuration;
        player.StartAttackCooldown();

        float halfArc = dm.BatArcAngle * 0.5f;

        if (player.Anim != null)
            player.Anim.SetTrigger("Attack");

        //배트 회전은 LanPlayerVisual이 돌린다 — 원격 화면·봇과 같은 코드다
        if (player.Visual != null)
        {
            player.Visual.PlayBatSwing();
            player.Visual.SendTrigger(LanPlayerVisual.ANIM_ATTACK);
        }

        BatDebugVisualizer.NotifySwing(player.transform, dm.BatRange * player.AuthorityScale, halfArc, swingDuration);
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
            player.Anim.ResetTrigger("Attack");

        //배트를 원위치·숨김 처리하는 것도 BatSwingRoutine이 끝내면서 한다
    }

    private void DetectBatHit()
    {
        // ═════════════════════════════════════════════
        //  히트 판정은 내 캐릭터에서만
        // ═════════════════════════════════════════════
        //
        // 이 조건이 깨지면 배트를 휘둘러도 아무도 안 맞고 스윙 연출만 돈다.
        NetIdentity myId = player.GetComponentInParent<NetIdentity>();

        if (myId != null)
        {
            if (!myId.IsMine)
                return;
            DetectBatHitLan(myId);
            return;
        }

    }

    /// <summary>
    /// [LAN] 스윙 궤적 안에 들어온 상대를 찾아 호스트에 판정을 요청한다.
    ///
    /// ★ 사람과 봇을 구분하지 않는다
    ///   둘 다 NetIdentity를 가진 네트워크 오브젝트라 한 갈래로 끝난다.
    ///   호스트의 ResolveBatHit이 거리·쿨다운·소유권을 다시 검사하므로
    ///   여기서는 '누구를 때렸다고 주장하는지'만 보내면 된다.
    /// </summary>
    private void DetectBatHitLan(NetIdentity myId)
    {
        var push = PushMode.Instance;
        if (push == null)
            return;

        var dm = DataManager.Instance;
        if (dm == null)
            return;

        float scale = player.AuthorityScale;
        float range = dm.BatRange * scale;
        Vector3 origin = player.transform.position
                         + Vector3.up * (player.Controller.height * 0.5f * scale);
        float halfArc = dm.BatArcAngle * 0.5f;

        //젤리(Edible 레이어)는 뺀다 — 아래 IsJelly에서 어차피 걸러진다
        int mask = LayerMask.GetMask("Player");
        Collider[] hits = Physics.OverlapSphere(origin, range, mask);

        foreach (var hit in hits)
        {
            if (hit.transform.root == player.transform.root)
                continue;

            Vector3 toTarget = hit.transform.position - player.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.001f)
                continue;

            if (Vector3.Angle(player.transform.forward, toTarget) > halfArc)
                continue;

            NetIdentity victim = hit.GetComponentInParent<NetIdentity>();
            if (victim == null || victim == myId)
                continue;

            // 젤리는 대상이 아니다(봇은 대상이다 — IsJelly가 IsBot으로 갈라준다)
            if (NetEntity.IsJelly(victim))
                continue;

            // 이미 판 밖인 봇은 건너뛴다
            AIPlayerMovement bot = victim.Bot;
            if (bot != null && bot.IsOutOfPlay)
                continue;

            hitDetected = true;
            push.RequestBatHit(victim.NetId, myId.NetId);
            return;
        }
    }
}
