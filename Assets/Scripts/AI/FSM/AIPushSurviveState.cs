using UnityEngine;
using UnityEngine.AI;
using JellyNet;

public class AIPushSurviveState : AIBaseState
{
    private float checkTimer;
    private bool fleeing;
    private float attackScanTimer;

    // ═══════════════════════════════════════════════════════
    //  ★ 반응 속도
    // ═══════════════════════════════════════════════════════
    //
    //  봇이 "멍청해 보이는" 이유의 상당 부분은 판단이 느려서다. 예전 값으로는
    //  상황 판단 0.15초 + 공격 탐색 0.3초 + 상태 재평가 0.4초가 겹쳐,
    //  최악의 경우 <b>사람이 사거리에 들어온 지 0.85초 뒤에야</b> 반응했다.
    //  그 사이 플레이어는 이미 등 뒤로 돌아가 있다.
    //
    //  판단 자체는 가벼운 연산이라(가장 가까운 상대 찾기 정도) 자주 돌려도 부담이 없다.
    private const float CHECK_INTERVAL = 0.06f;
    private const float ATTACK_SCAN_INTERVAL = 0.1f;

    public AIPushSurviveState(AIPlayerMovement ai) : base(ai) { }

    public override void Enter()
    {
        checkTimer = 0f;
        fleeing = false;
        attackScanTimer = 0f;
        ai.ApplyStateSpeed();
        ai.Agent.stoppingDistance = 0.3f;
        ai.Agent.ResetPath();
        ai.Agent.velocity = Vector3.zero;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return;
        if (ai.IsDashing || ai.IsAttacking)
            return;

        checkTimer += Time.deltaTime;
        if (checkTimer < CHECK_INTERVAL)
            return;

        //이번 판단이 실제로 몇 초 만에 돌아왔는지. 아래 공격 스캔 주기가 이 값을 쓴다.
        //예전엔 상수 CHECK_INTERVAL을 그대로 더해서, 프레임이 길거나 도주 분기로
        //빠진 틱이 있으면 공격 스캔 간격이 실제 시간과 조용히 어긋났다
        float sinceLastCheck = checkTimer;
        checkTimer = 0f;

        var collapse = TileCollapseManager.Instance;
        if (collapse == null)
            return;

        bool onDanger = collapse.IsFootingUnsafe(ai.transform.position);

        if (onDanger)
        {
            // ★ 도망치면서도 사거리 안이면 친다.
            //
            //   예전엔 위험 타일 위에 있으면 <b>도망만</b> 갔다. 그런데 밀치기 모드는
            //   시간이 갈수록 발판이 닳아 '위험한 칸'이 늘어난다. 결국 봇은 판 내내
            //   도망만 다니고 한 번도 공격하지 않는다.
            //   배트를 휘두르는 건 제자리 동작이라 도피와 동시에 할 수 있다.
            TryOpportunisticSwing();

            // ★ '발밑이 닳았다'와 '위협에서 도망친다'는 다른 사건이다
            //   예전엔 발밑이 위험해지면 무조건 가장 가까운 상대에게서 <b>멀어지는</b>
            //   칸으로 갔다. 밀치기 모드에서 그 상대는 대개 자기가 싸우던 대상이라,
            //   발판이 닳을 때마다 교전을 통째로 접고 도망쳤다. 그러면 봇은 판 내내
            //   붙었다 도망쳤다만 반복하고 아무도 떨어뜨리지 못한다.
            //
            //   싸울 상대가 있으면 <b>상대는 붙잡아 두고 발판만 갈아탄다</b>(재배치).
            //   그게 곧 치고 빠지기다 — 한 칸에 머무는 시간이 제자리 마모 주기를 넘지
            //   않으므로 자기 발밑에서 죽지도 않는다.
            //   상대가 없을 때만 예전처럼 '위협에서 먼 곳'으로 도망친다.
            if (TryReposition())
                return;

            Vector3 threat = NearestThreatPos();

            // ★ 마지막 수단까지 내려간다 — 제자리가 언제나 최악이기 때문이다
            //   예전엔 여기서 두 번째 탐색(avoidDangerous: true)까지만 하고 실패하면
            //   return이었다. 그런데 그 두 탐색은 <b>둘 다 위험한 칸을 후보에서 뺀다.</b>
            //   후반에 주변이 전부 닳으면 둘 다 빈손으로 돌아오고, 봇은 곧 꺼질 칸 위에
            //   선 채로 판단을 포기했다.
            //
            //   서 있는 칸은 이미 '발밑 위험'이라 가장 먼저 무너진다. 옆 칸이 아무리
            //   닳았어도 그보다는 오래 간다. 그래서 세 번째로 '위험해도 발판이 남은
            //   가장 가까운 칸'을 찾는다. FindNearestSafeTile은 고리 1부터 보므로
            //   지금 서 있는 칸을 다시 고르는 일은 없다.
            Vector3 safePos;
            if (!collapse.FindEscapeTile(ai.transform.position, threat, out safePos)
                && !collapse.FindNearestSafeTile(ai.transform.position, out safePos, avoidDangerous: true)
                && !collapse.FindNearestSafeTile(ai.transform.position, out safePos, avoidDangerous: false))
                return;

            // 속도는 플레이어와 동일(moveSpeed)하게 유지한다. 위험(무너지는 발판) 회피는
            // 속도 부스트가 아니라 아래 TryDash(짧은 대쉬 버스트)로 처리한다.
            // ★ 여기만 TrySetSafePath를 쓰지 않는다 — 의도적이다.
            //   이미 무너지는 발판 위에 서 있는 상황이라, "경로가 위험 구간을 지난다"는
            //   이유로 탈출을 막으면 제자리에서 같이 떨어진다. 나가는 길은 막지 않는다.
            if (NavMesh.SamplePosition(safePos, out NavMeshHit hit, 5f, ai.NavFilter))
            {
                ai.CachedPath.ClearCorners();
                if (ai.Agent.CalculatePath(hit.position, ai.CachedPath)
                    && ai.CachedPath.status == NavMeshPathStatus.PathComplete)
                {
                    ai.Agent.SetPath(ai.CachedPath);
                    fleeing = true;
                    ai.TryDash();
                }
            }
        }
        else if (fleeing)
        {
            ai.Agent.ResetPath();
            ai.Agent.velocity = Vector3.zero;
            ai.ApplyStateSpeed();
            fleeing = false;
        }
        else
            UpdateCombatOrWander(sinceLastCheck);
    }

    /// <summary>
    /// 안전한 상태일 때의 행동. 근처에 타겟이 있으면 추격/공격, 없으면 위험 타일을
    /// 피해 평소처럼 배회(roam)한다.
    /// </summary>
    private void UpdateCombatOrWander(float deltaSinceLastCheck)
    {
        attackScanTimer += deltaSinceLastCheck;
        if (attackScanTimer < ATTACK_SCAN_INTERVAL)
            return;
        attackScanTimer = 0f;

        Transform target = FindNearestTarget();
        if (target != null && TryEngageTarget(target))
            return;

        // 타겟이 없거나 추격 범위 밖 → 평소엔 배회
        Wander();
    }

    // ─────────────────────────────────────────────────────────
    //  조준 유예 — 사람이 반응할 틈
    // ─────────────────────────────────────────────────────────
    //
    // ★ 예전엔 사거리에 들어오는 <b>즉시</b> 휘둘렀다
    //   봇은 매 판단마다 거리를 재고 조건이 맞으면 바로 TryAttack을 부른다.
    //   사람은 상대가 다가오는 걸 보고 반응해야 하는데, 봇에겐 그 지연이 없다.
    //   결과적으로 <b>먼저 붙는 쪽이 무조건 이기는 싸움</b>이 됐다.
    //
    //   사거리에 들어오면 바로 치지 않고 그 자리에서 상대를 바라보며 잠깐 겨눈다.
    //   그동안 사람은 물러나거나 먼저 칠 수 있다.
    //   겨누는 동안 상대가 사거리 밖으로 나가면 유예는 초기화된다 —
    //   붙었다 떨어졌다 하는 것만으로 봇을 계속 헛치게 만들 수 있다.
    [Tooltip("사거리에 들어온 뒤 실제로 휘두르기까지의 시간(초). 사람이 반응할 틈이다.")]
    private const float AIM_DELAY = 0.45f;

    //겨누기 시작한 시각. 사거리 밖으로 나가면 -1로 되돌린다
    private float aimStartTime = -1f;

    /// <summary>타겟을 추격하거나 사거리 안이면 공격. 무언가 행동했으면 true.</summary>
    private bool TryEngageTarget(Transform target)
    {
        Vector3 dirToTarget = target.position - ai.transform.position;
        dirToTarget.y = 0;
        float dist = dirToTarget.magnitude;

        var dm = DataManager.Instance;
        float range = dm != null ? dm.BatRange * ai.GetMyAuthorityScale() : 2f;

        // ── 사거리 안: 돌아서서 친다 ──
        //
        //   ★ 경로를 먼저 지운다.
        //     경로가 남아 있으면 AIPlayerMovement.Update가 매 프레임
        //     "가려는 방향"으로 회전을 덮어쓴다. 그러면 FaceTarget으로 맞춰둔 각도가
        //     바로 풀려 배트 판정(전방 ±60°)을 빗나간다.
        //     휘두르는 동안은 제자리에서 상대를 보고 있어야 한다.
        if (dist <= range * 1.2f)
        {
            // ★ 각이 없으면 치기 전에 자리를 옮겨 본다
            //   여기서 휘둘러 봐야 상대는 옆 칸으로 밀릴 뿐이다. 서로 그러고 있으면
            //   마주 보고 굳은 채 아무도 떨어지지 않는다 — 봇이 멍청해 보이는 그림이다.
            //
            //   <b>다만 각이 없다고 안 치지는 않는다.</b> 그러면 구멍이 없는 초반에
            //   봇이 완전히 수동적이 되고, 때리는 것 자체에도 값이 있다
            //   (BatHitGrowth로 커지고 pushHitScore가 붙는다).
            //   그래서 "각이 나오는 칸이 근처에 있으면 그리로 옮기고, 없으면 그냥 친다"로 둔다.
            //   옮기는 동안 봇이 상대 주위를 도는 그림이 나오고, 후반에 구멍이 늘수록
            //   각이 자주 잡혀 실제로 서로를 떨어뜨리게 된다.
            //
            //   이미 어디론가 가는 중이면 다시 고르지 않는다 — 매 틱 목적지를 바꾸면
            //   제자리에서 떨리기만 한다.
            bool standing = !ai.Agent.pathPending && !ai.Agent.hasPath;
            if (standing
                && !HasPushOff(ai.transform.position, target.position)
                && TryRepositionFor(target))
                return true;

            if (ai.Agent.enabled && ai.Agent.isOnNavMesh && ai.Agent.hasPath)
                ai.Agent.ResetPath();
            ai.Agent.velocity = Vector3.zero;

            //겨누는 동안에도 상대를 계속 본다. 사람 눈에는 '노리고 있다'로 읽힌다
            FaceTarget(dirToTarget);

            if (aimStartTime < 0f)
                aimStartTime = Time.time;

            if (Time.time - aimStartTime >= AIM_DELAY)
            {
                ai.TryAttack();
                aimStartTime = -1f;      //다음 스윙은 처음부터 다시 겨눈다
            }

            return true;
        }

        //사거리를 벗어났다 → 겨누던 것을 접는다
        aimStartTime = -1f;

        // ── 추격 ──
        //
        //   ★ 추격 범위를 감지 반경 전체로 넓혔다.
        //     예전엔 detectRadius * 0.6 안에 들어와야 쫓았다. 그 밖이면 상대를
        //     <b>보고 있으면서도 딴 데로 배회</b>했다. 눈앞에서 어슬렁거리는 것처럼 보인다.
        //
        //   ★ 목표 지점은 '지금 위치'가 아니라 '갈 곳'으로 잡는다.
        //     상대가 움직이는 동안 현재 위치로만 경로를 잡으면 계속 뒤를 따라간다.
        //     조금 앞을 노리면 실제로 따라잡는다.
        if (dist < ai.DetectRadius)
        {
            Vector3 aim = target.position + PredictLead(target, dist);

            if (NavMesh.SamplePosition(aim, out NavMeshHit hit, 5f, ai.NavFilter)
                || NavMesh.SamplePosition(target.position, out hit, 5f, ai.NavFilter))
            {
                ai.ApplyStateSpeed();

                if (TrySetSafePath(hit.position))
                {
                    // 거리가 꽤 벌어져 있으면 대쉬로 붙는다(원래 도주에만 쓰던 것을 공격에도).
                    if (dist > range * 3f)
                        ai.TryDash();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 상대의 이동을 감안해 조금 앞을 노린다.
    /// 거리가 멀수록 더 앞을 본다(도달까지 시간이 걸리므로).
    /// </summary>
    private Vector3 PredictLead(Transform target, float dist)
    {
        if (ai.MoveSpeed <= 0.01f)
            return Vector3.zero;

        Vector3 vel = Vector3.zero;

        CharacterController cc = target.GetComponentInChildren<CharacterController>();
        if (cc != null && cc.enabled) 
            vel = cc.velocity;
        else
        {
            NavMeshAgent ag = target.GetComponentInChildren<NavMeshAgent>();
            if (ag != null && ag.enabled)
                vel = ag.velocity;
        }

        vel.y = 0f;
        if (vel.sqrMagnitude < 0.01f)
            return Vector3.zero;

        float travelTime = Mathf.Clamp(dist / ai.MoveSpeed, 0f, 0.7f);
        return vel * travelTime;
    }

    /// <summary>
    /// 가장 가까운 상대의 위치. 도망 방향을 정할 때 쓴다.
    /// 아무도 없으면 자기 위치를 돌려줘서 "위협 없음 = 거리만 본다"가 되게 한다.
    /// </summary>
    private Vector3 NearestThreatPos()
    {
        float best = float.MaxValue;
        Vector3 pos = ai.transform.position;

        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null || e.Transform == ai.transform || e.IsOutOfPlay)
                continue;
            float d = Vector3.Distance(ai.transform.position, e.Transform.position);
            if (d < best)
            {
                best = d;
                pos = e.Transform.position;
            }
        }

        return pos;
    }

    /// <summary>
    /// 도망 중이라도 사거리 안에 상대가 있으면 한 대 친다.
    /// 스윙은 제자리 동작이라 이동을 방해하지 않는다.
    /// </summary>
    private void TryOpportunisticSwing()
    {
        var dm = DataManager.Instance;
        if (dm == null)
            return;

        float range = dm.BatRange * ai.GetMyAuthorityScale();

        Transform target = FindNearestTarget();
        if (target == null)
            return;

        Vector3 d = target.position - ai.transform.position;
        d.y = 0f;

        if (d.magnitude > range * 1.2f)
        {
            aimStartTime = -1f;
            return;
        }

        FaceTarget(d);

        //도망치면서 치는 것도 같은 유예를 지킨다. 여기만 즉발이면 사람은
        //'도망가는 봇에게 스치기만 해도 맞는' 상황을 겪는다
        if (aimStartTime < 0f)
            aimStartTime = Time.time;

        if (Time.time - aimStartTime < AIM_DELAY)
            return;

        ai.TryAttack();
        aimStartTime = -1f;
    }

    // ─────────────────────────────────────────────────────────
    //  재배치 — 상대는 붙잡아 두고 발판만 갈아탄다
    // ─────────────────────────────────────────────────────────

    [Tooltip("재배치할 때 훑어볼 주변 칸 반경")]
    private const int REPOSITION_SEARCH_CELLS = 2;

    //점수 계산에 쓰는 값. 델리게이트가 매번 새 클로저를 만들지 않도록 필드로 둔다
    private Vector3 repositionTargetPos;
    private float repositionPreferredDist;
    private Vector3 repositionFromPos;
    private System.Func<Vector3, float> repositionScorer;

    /// <summary>
    /// 발밑이 닳았지만 싸울 상대가 있을 때, 상대를 놓지 않는 선에서 옆 칸으로 옮긴다.
    /// 옮길 곳을 못 찾으면 false — 그때는 호출부가 평소의 도주로 내려간다.
    /// </summary>
    private bool TryReposition()
    {
        Transform target = FindNearestTarget();
        return target != null && TryRepositionFor(target);
    }

    /// <summary>지정한 상대를 붙잡아 둔 채 옆 칸으로 옮긴다.</summary>
    private bool TryRepositionFor(Transform target)
    {
        var collapse = TileCollapseManager.Instance;
        var dm = DataManager.Instance;
        if (collapse == null || dm == null)
            return false;

        repositionTargetPos = target.position;
        repositionFromPos = ai.transform.position;

        //배트가 닿는 거리에 서고 싶다. 여기에 서 있어야 다음 스윙이 가능하다
        repositionPreferredDist = dm.BatRange * ai.GetMyAuthorityScale();

        repositionScorer ??= ScoreRepositionTile;

        if (!collapse.FindBestFooting(repositionFromPos, REPOSITION_SEARCH_CELLS,
                                      repositionScorer, out Vector3 spot))
            return false;

        if (!NavMesh.SamplePosition(spot, out NavMeshHit hit, 5f, ai.NavFilter))
            return false;

        ai.CachedPath.ClearCorners();
        if (!ai.Agent.CalculatePath(hit.position, ai.CachedPath)
            || ai.CachedPath.status != NavMeshPathStatus.PathComplete)
            return false;

        //발밑이 이미 닳은 상황이라 경로 위험 검사로 막지 않는다 — 나가는 길은 열어둔다
        //(바로 아래 도주 분기가 같은 이유로 TrySetSafePath를 쓰지 않는다)
        ai.Agent.SetPath(ai.CachedPath);
        ai.ApplyStateSpeed();
        fleeing = true;   //안전한 칸에 닿으면 위 else 분기가 경로를 정리한다
        return true;
    }

    /// <summary>
    /// 재배치 후보 칸의 점수. 높을수록 좋다.
    ///   · 상대와의 거리가 배트 사거리에 가까울수록 가점 — 붙잡아 두기 위해서다
    ///   · 내가 멀리 가야 할수록 감점 — 가는 동안 지금 칸이 꺼진다
    /// </summary>
    private float ScoreRepositionTile(Vector3 tileCenter)
    {
        float distToTarget = Vector3.Distance(tileCenter, repositionTargetPos);
        float travel = Vector3.Distance(tileCenter, repositionFromPos);

        float score = -Mathf.Abs(distToTarget - repositionPreferredDist) - travel * TravelCostWeight;

        //이 칸에서 밀면 상대가 떨어진다면 그쪽으로 돌아가는 값이 있다.
        //가점을 거리 항보다 크게 둬야 "조금 멀어도 각이 나오는 칸"을 고른다
        if (HasPushOff(tileCenter, repositionTargetPos))
            score += PushOffBonus;

        return score;
    }

    //각이 나오는 칸에 주는 가점. 배트 사거리(크기 2에서 4m)보다 넉넉히 커야
    //거리 항을 이기고 실제로 각을 보고 자리를 잡는다
    private const float PushOffBonus = 25f;

    //"상대와의 거리를 1m 맞추는 것"이 "내가 1m 더 뛰는 것"의 몇 배 가치인가.
    //1보다 작게 둔 이유는 가까운 칸을 우선하기 위해서다 — 멀리 가면 가는 도중에 꺼진다
    private const float TravelCostWeight = 0.6f;

    // ─────────────────────────────────────────────────────────
    //  ★ 밀어 떨어뜨릴 각
    // ─────────────────────────────────────────────────────────
    //
    //  맵 한복판에서 서로 때려봐야 옆 칸으로 밀릴 뿐 아무 일도 안 일어난다.
    //  "가까우면 친다"로만 움직이면 봇들은 그 무의미한 교착에 갇힌다 —
    //  마주 보고 서서 서로 휘두르기만 하고 아무도 떨어지지 않는다.
    //
    //  그래서 <b>밀었을 때 상대가 빈 칸이나 맵 밖으로 갈 때만</b> 붙는다.
    //  각이 안 나오면 치지 않고 각이 나오는 칸으로 옮긴다. 그 '옮김'이
    //  봇을 움직이게 하고, 후반에 구멍이 늘수록 각이 자주 나와 서로를 떨어뜨린다.

    /// <summary>이 봇이 한 대 쳤을 때 상대가 밀려나는 거리(월드 미터).</summary>
    private float KnockbackDistance()
    {
        var dm = DataManager.Instance;
        if (dm == null)
            return 0f;

        //PushMode.HostJudgeBatHit과 같은 식이다 — 힘은 기준 크기 대비 배수다
        float force = dm.BatPushForce
                    * (ai.GetMyAuthorityScale() / Mathf.Max(0.01f, NetEntity.BaselineScale));

        //Knockback은 force에서 0까지 DURATION 동안 선형으로 준다 → 이동거리는 그 삼각형 넓이
        return force * Knockback.DURATION * 0.5f;
    }

    /// <summary><paramref name="from"/>에 서서 상대를 밀면 떨어뜨릴 수 있는가.</summary>
    private bool HasPushOff(Vector3 from, Vector3 targetPos)
    {
        var collapse = TileCollapseManager.Instance;
        return collapse != null && collapse.HasPushOff(from, targetPos, KnockbackDistance());
    }

    /// <summary>위험 타일을 피해 무작위 안전 지점으로 배회한다.</summary>
    private void Wander()
    {
        // 이미 경로를 따라 이동 중이면 목적지 도착/경로 소실 전까진 그대로 둔다.
        if (ai.Agent.pathPending)
            return;
        if (ai.Agent.hasPath && ai.Agent.remainingDistance > ai.Agent.stoppingDistance + 0.5f)
            return;

        ai.ApplyStateSpeed();

        if (ai.TryGetWanderDestination(out Vector3 dest)
            && NavMesh.SamplePosition(dest, out NavMeshHit hit, 5f, ai.NavFilter))
        {
            TrySetSafePath(hit.position);
        }
    }

    // 자기보다 '작은' 먹잇감만 추격 대상으로 본다.
    // 예전엔 크기 무관 최근접 엔티티(같은 크기의 다른 봇 포함)를 쫓아서, 봇끼리 서로를 추격하며
    // 한 타일로 눈덩이처럼 뭉쳤다 → 그 타일이 step 마모로 '무너지기 직전'이 되어 다 같이 추락했다.
    // 더 큰 상대는 어차피 FindThreat→FleeState가 처리(도주)하므로, 여기서 동급/대형을 빼면
    // 봇끼리 서로 끌어당겨 뭉치는 일이 사라진다(포식자-피식자 관계만 남아 자연히 분산).
    private Transform FindNearestTarget()
    {
        float bestDist = float.MaxValue;
        Transform best = null;
        float myScale = ai.GetMyAuthorityScale();

        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null || e.Transform == ai.transform)
                continue;
            if (e.IsOutOfPlay)
                continue; // 탈락/흡수 판정 단일 출처 (G6/K2)

            // ★ 크기 조건은 봇에게만 건다 — 사람은 크기와 무관하게 대상이다.
            //
            //   밀치기 모드에는 <b>잡아먹히는 개념이 없다.</b> 큰 상대라고 피할 이유가 없고,
            //   오히려 큰 상대일수록 밀어 떨어뜨릴 가치가 있다.
            //
            //   예전엔 사람도 크기로 걸렀는데 그게 이런 악순환을 만들었다:
            //     플레이어가 배트를 맞힌다 → 커진다 → 봇의 대상에서 빠진다
            //     → 봇이 공격을 못 한다 → 봇은 안 커진다 → 격차가 더 벌어진다
            //   한 번 맞기 시작하면 봇이 영원히 반격하지 못하는 구조였다.
            //
            //   봇끼리만 크기를 보는 건 서로 추격하며 한 타일에 뭉치는 것을
            //   막기 위한 별개의 규칙이다. 그래서 이 한 줄만 IsBot으로 갈린다.
            if (e.IsBot && e.ScaleValue >= myScale)
                continue;

            float d = Vector3.Distance(ai.transform.position, e.Transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = e.Transform;
            }
        }

        return bestDist < ai.DetectRadius ? best : null;
    }

    private void FaceTarget(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.01f)
            return;
        ai.transform.rotation = Quaternion.LookRotation(direction.normalized);
    }

    public override void Exit()
    {
        ai.ApplyStateSpeed();
        ai.Agent.stoppingDistance = 0f;
    }
}
