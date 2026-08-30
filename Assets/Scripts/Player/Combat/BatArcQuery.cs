using UnityEngine;
using JellyNet;

/// <summary>
/// 배트 스윙 궤적 안에 들어온 상대를 찾는다. <b>사람과 봇이 같은 판정을 쓴다.</b>
///
/// ★ 왜 한 곳으로 모았나
///   같은 판정이 두 벌 있었다 — PlayerAttackState.DetectBatHitLan(42줄)과
///   AIPlayerMovement.DetectBatHit(56줄). 사거리·각도·레이어·자기 자신 제외까지
///   전부 같은데 몸 높이를 읽는 줄 하나만 달랐다.
///   두 벌이면 한쪽만 고쳐지는 일이 반드시 생기고, 실제로 그랬다:
///     · 젤리 제외(IsJelly)가 사람 쪽에만 있었다
///     · <b>판 밖 상대 제외가 사람 쪽에만 있었다</b> → 봇은 초콜릿에 빠진 시체를 계속 때렸다
///   이제 한 벌이라 그런 자리가 없다.
///
/// ★ 판정만 하고 결과는 돌려주기만 한다
///   '누구에게 무엇을 요청할지'는 부르는 쪽이 다르다.
///     사람 → PushMode.RequestBatHit (내가 맞혔다고 호스트에 주장)
///     봇   → PushMode.HostBotBatHit (호스트가 직접 확정)
///   그 차이만 밖에 남기고, 공통인 '누가 맞았나'를 여기서 답한다.
/// </summary>
public static class BatArcQuery
{
    // 캐릭터당 대표 콜라이더 하나만 세므로 인원수만큼이면 넉넉하다.
    // (예전엔 캡슐+메시가 둘 다 걸려 32가 필요했다 — GameTags.IsCharacterMainCollider 참고)
    private static readonly Collider[] hitBuffer = new Collider[16];

    private static int playerMask = -1;

    private static int PlayerMask
    {
        get
        {
            //젤리(Edible)는 넣지 않는다 — 배트는 Push 전용이고 젤리는 대상이 아니다.
            //아래 IsJelly에서 어차피 걸러지지만, 마스크에서 빼면 훑는 양 자체가 준다
            if (playerMask < 0)
                playerMask = GameLayers.PlayerMask;
            return playerMask;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMask() => playerMask = -1;

    /// <summary>
    /// 스윙 궤적 안의 상대 하나를 돌려준다. 없으면 null.
    /// </summary>
    /// <param name="attacker">휘두르는 쪽의 루트 Transform</param>
    /// <param name="attackerId">휘두르는 쪽의 신원(자기 자신 제외에 쓴다)</param>
    /// <param name="scale">판정에 쓰는 크기. transform.localScale이 아니라 AuthorityScale/ScaleValue다</param>
    public static NetIdentity Find(Transform attacker, NetIdentity attackerId, float scale)
    {
        DataManager dm = DataManager.Instance;

        if (dm == null || attacker == null || attackerId == null)
            return null;

        float range = dm.BatRange * scale;
        float halfArc = dm.BatArcAngle * 0.5f;

        //몸 높이는 NavMesh 에이전트 타입의 authoring 값을 쓴다.
        //예전엔 사람이 CharacterController.height, 봇이 NavMeshAgent.height를 읽어
        //같은 판정인데 기준 높이가 갈릴 수 있었다
        float bodyHeight = NavMeshUtil.AgentHeight(0);
        Vector3 origin = attacker.position + Vector3.up * (bodyHeight * 0.5f * scale);

        int count = Physics.OverlapSphereNonAlloc(origin, range, hitBuffer, PlayerMask);

        for (int i = 0; i < count; i++)
        {
            Collider hit = hitBuffer[i];

            if (hit == null || hit.transform.root == attacker.root)
                continue;

            // ★ 캐릭터당 대표 콜라이더 하나만 본다
            //   캐릭터는 루트 캡슐과 자식 메시가 둘 다 Player 레이어 트리거라 각각 걸린다.
            //   그냥 두면 같은 상대를 두 번 검사하는 낭비에 그치지 않는다 — 두 콜라이더는
            //   <b>transform.position이 서로 다르다</b>(캡슐은 루트, 메시는 자식).
            //   아래 각도 판정이 어느 쪽이 먼저 스캔됐느냐에 따라 다른 답을 낼 수 있었다.
            if (!GameTags.IsCharacterMainCollider(hit))
                continue;

            NetIdentity victim = hit.GetComponentInParent<NetIdentity>();

            if (victim == null || victim == attackerId)
                continue;

            //각도는 콜라이더가 아니라 <b>상대의 루트</b>를 기준으로 잰다.
            //콜라이더 위치를 쓰면 메시의 오프셋만큼 판정이 틀어진다
            Vector3 toTarget = victim.transform.position - attacker.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.001f)
                continue;

            if (Vector3.Angle(attacker.forward, toTarget) > halfArc)
                continue;

            //젤리는 대상이 아니다(봇은 대상이다 — IsJelly가 IsBot으로 갈라준다)
            if (NetEntity.IsJelly(victim))
                continue;

            //이미 탈락했거나 흡수당하는 중인 상대는 때려도 의미가 없다
            if (NetEntity.IsOutOfPlay(victim))
                continue;

            return victim;
        }

        return null;
    }
}
