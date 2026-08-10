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
        JellyNet.LanPlayerVisual.ReportTrigger(player, JellyNet.LanPlayerVisual.ANIM_ATTACK);

        // [LAN 이식] 공격 애니메이션 전파는 위 ReportTrigger가 담당한다.
        //   아래 Photon RPC는 photonView가 없어 실행되지 않는다(레거시 브랜치용).
        NetworkPlayerSync netSync = player.GetComponent<NetworkPlayerSync>();
        if (netSync != null && netSync.photonView != null && netSync.photonView.IsMine)
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
        // ═════════════════════════════════════════════
        //  [LAN 이식] 히트 판정은 내 캐릭터에서만
        // ═════════════════════════════════════════════
        //
        // ★ 예전 조건이 통째로 죽어 있었다
        //   NetworkPlayerSync를 걷어낸 뒤로 mySync가 항상 null → 여기서 바로 return.
        //   즉 <b>배트를 휘둘러도 아무도 맞지 않았다.</b> 스윙 연출만 돌았다.
        JellyNet.NetIdentity myId = player.GetComponentInParent<JellyNet.NetIdentity>();

        if (myId != null)
        {
            if (!myId.IsMine) return;
            DetectBatHitLan(myId);
            return;
        }

        NetworkPlayerSync mySync = player.GetComponent<NetworkPlayerSync>();
        if (mySync == null || mySync.photonView == null || !mySync.photonView.IsMine) return;

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

    /// <summary>
    /// [LAN] 스윙 궤적 안에 들어온 상대를 찾아 호스트에 판정을 요청한다.
    ///
    /// ★ 사람과 봇을 구분하지 않는다
    ///   Photon판은 NetworkPlayerSync / AIPlayerMovement를 따로 찾아 다른 RPC를 썼다.
    ///   LAN에서는 둘 다 NetIdentity를 가진 네트워크 오브젝트라 한 갈래로 끝난다.
    ///   호스트의 ResolveBatHit이 거리·쿨다운·소유권을 다시 검사하므로
    ///   여기서는 '누구를 때렸다고 주장하는지'만 보내면 된다.
    /// </summary>
    private void DetectBatHitLan(JellyNet.NetIdentity myId)
    {
        var push = JellyNet.PushMode.Instance;
        if (push == null) return;

        var dm = DataManager.Instance;
        if (dm == null) return;

        float scale = player.transform.localScale.x;
        float range = dm.batRange * scale;
        Vector3 origin = player.transform.position
                         + Vector3.up * (player.controller.height * 0.5f * scale);
        float halfArc = dm.batArcAngle * 0.5f;

        int mask = LayerMask.GetMask("Player") | LayerMask.GetMask("Edible");
        Collider[] hits = Physics.OverlapSphere(origin, range, mask);

        foreach (var hit in hits)
        {
            if (hit.transform.root == player.transform.root) continue;

            Vector3 toTarget = hit.transform.position - player.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.001f) continue;

            if (Vector3.Angle(player.transform.forward, toTarget) > halfArc) continue;

            JellyNet.NetIdentity victim = hit.GetComponentInParent<JellyNet.NetIdentity>();
            if (victim == null || victim == myId) continue;

            // 젤리는 대상이 아니다(봇은 대상이다 — IsBot으로 구분된다)
            if (!victim.IsBot && victim.PrefabId >= JellyNet.NetConfig.JELLY_PREFAB_START) continue;

            // 이미 판 밖인 봇은 건너뛴다
            AIPlayerMovement bot = victim.GetComponent<AIPlayerMovement>();
            if (bot != null && bot.IsOutOfPlay) continue;

            _hitDetected = true;
            push.RequestBatHitPublic(victim.NetId, myId.NetId);
            return;
        }
    }
}
