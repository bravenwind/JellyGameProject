using UnityEngine;
using UnityEngine.AI;
using JellyNet;

public class AIPushSurviveState : AIBaseState
{
    private float checkTimer;
    private bool fleeing;
    private float attackScanTimer;

    //각을 잡으러 이동하는 중인가. 도착 전까지 교전 정지 코드로 내려가지 않는다
    private bool repositioning;

    // ★ 각 잡기에 쿨다운을 둔다 — 돌기만 하고 안 때리는 것을 막는다
    //   상대가 계속 움직이면 도착할 때마다 각이 다시 어긋나 또 돌게 된다.
    //   그러면 봇은 평생 상대 주위를 맴돌기만 하고 한 대도 안 친다.
    //   한 번 돌고 나면 잠깐은 각을 따지지 않고 그 자리에서 겨눠 친다.
    //   AIM_DELAY(0.45초) + 스윙이 한 번은 들어가는 길이여야 한다.
    private const float ORBIT_COOLDOWN = 1.5f;
    private float nextOrbitTime;

    // 각 잡기 이동에 거는 시간 상한. 끼임 감지가 먼저 걸리는 게 보통이지만,
    // 아주 느리게 밀려나는 등 velocity가 0이 아닌 채 도착이 안 되는 경우도 막는다.
    private const float REPOSITION_TIMEOUT = 2.5f;
    private float repositionDeadline;

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
        ResetStuck();
        checkTimer = 0f;
        fleeing = false;
        repositioning = false;
        nextOrbitTime = 0f;
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

        // ★ 끼임 감지를 여기에도 붙인다 — 네 상태 중 여기만 빠져 있었다
        //   (AIChaseState·AIFleeState·AIWanderState는 전부 부르고 있다)
        //   구석에 몰리거나 몸끼리 밀리면 경로는 있는데 속도가 0인 상태가 이어진다.
        //   그걸 푸는 곳이 없으면 봇은 그 경로를 붙잡고 영영 서 있는다.
        //   여기에 이번 repositioning 래치까지 얹히면 <b>도착 판정이 영원히 false</b>가
        //   되어 겨누지도 치지도 다시 판단하지도 않는 완전 정지가 된다.
        if (HandleStuck())
        {
            repositioning = false;   //붙잡고 있던 재배치도 같이 접는다
            return;
        }

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

            // ★ 이미 안전한 곳으로 가는 중이면 그 길을 지킨다
            //   판단은 0.06초마다 돈다. 그때마다 후보를 다시 고르면 점수가 조금만
            //   흔들려도(상대가 움직이거나 옆 칸이 닳거나) 목적지가 바뀌어, 봇이
            //   방향을 계속 갈아타며 제자리에서 떤다.
            //   목적지가 아직 안전하고 거기 닿지 않았다면 다시 고를 이유가 없다.
            //가던 목적지가 아직 쓸 만하면(붕괴중이 아니면) 그 길을 지킨다
            if (fleeing && ai.Agent.hasPath && !ReachedDestination()
                && collapse.StepsAfterArrival(ai.Agent.destination) >= 0)
                return;

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

            // ★ 후보를 하나만 보고 포기하지 않는다
            //   예전엔 후보를 하나 고른 뒤 `CalculatePath && PathComplete` 하나로 끝냈다.
            //   주변이 무너져 NavMesh가 잘려 완주 경로가 안 나오면 아무 일도 일어나지
            //   않았고, 봇은 꺼지는 발판 위에 선 채로 판단을 포기했다.
            //   ("사방이 위험한데 플레이어가 붙으면 아예 멈춘다"가 이것이다)
            //
            //   이제 셋을 모아 두 바퀴 돈다. 먼저 <b>남의 빨간 칸을 안 지나는</b> 길만
            //   찾고, 그런 게 없을 때만 위험을 감수한다. 그것도 없으면 부분 경로로라도
            //   움직인다. 서 있으면 확실히 죽고, 움직이면 죽을 수도 있을 뿐이다.
            Vector3 threat = NearestThreatPos();

            escapeCandidateCount = 0;
            AddEscapeCandidate(collapse.FindEscapeTile(ai.transform.position, threat, out Vector3 a), a);
            AddEscapeCandidate(collapse.FindNearestSafeTile(ai.transform.position, out Vector3 b, avoidDangerous: true), b);
            //격자 끝까지 넓혀 가며 찾는 폴백 둘. 위 탐색은 반경 6칸 상자라
            //그 안에 쓸 칸이 하나도 없을 때를 대비한다
            AddEscapeCandidate(collapse.FindNearestSafeTile(ai.transform.position, out Vector3 c, avoidDangerous: false), c);

            if (escapeCandidateCount == 0)
                return;

            if (TryEscapeToAny(allowDangerousCrossing: false)
                || TryEscapeToAny(allowDangerousCrossing: true)
                || TryPartialEscapeToAny())
            {
                fleeing = true;

                // ★ 경로의 끝이 설 수 있는 칸일 때만 대쉬한다
                //   부분 경로는 구멍 앞에서 끊긴다. 그 끝으로 80m/s로 뛰면
                //   되돌릴 시간이 없다. 걸어가면 가는 도중에 다른 칸이 보여
                //   다시 판단할 기회가 남는다.
                if (PathEndsOnFooting())
                    ai.TryDash();
            }
        }
        else if (fleeing)
        {
            // ★ '이제 안전하다'고 그 자리에 멈추면 경계선 위에서 떤다
            //   위험 판정은 transform.position <b>점 하나</b>를 보는데, 마모는 몸
            //   발자국(반지름 = PlayerJellyRadius × 크기 ≈ 1.3m)을 본다. 그래서
            //   경계선 ±1.3m 구간에는 "중심은 안전한데 몸은 아직 빨간 칸을 밟고 있는"
            //   자리가 있다.
            //
            //   예전엔 중심이 경계를 넘는 <b>그 순간</b> ResetPath로 세웠다. 봇은 몸을
            //   반쯤 걸친 채 멈추고, 서 있으니 발밑이 깎이고, 에이전트 정렬이나 다른
            //   봇에 밀려 중심이 도로 넘어가면 다시 도망 경로를 깔았다. 경계선을
            //   사이에 두고 진동하다 무너지는 칸과 함께 떨어진다.
            //   ("무너지는 타일과 멀쩡한 타일을 계속 왔다갔다 하다 죽는다"가 이것이다)
            //
            //   목적지는 칸의 중심이므로 거기까지 가면 몸이 위험 칸에서 완전히 빠진다.
            //   경로를 잃었으면 ReachedDestination이 true라 여기서 막히지 않는다.
            if (!ReachedDestination())
                return;

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
            // ★ 각을 잡으러 가는 중이면 그 이동을 <b>끊지 않는다</b>
            //   처음엔 "경로가 없을 때만 새로 고른다"로만 막았는데, 그것만으로는
            //   봇이 통째로 얼어붙었다. 경로를 깐 다음 틱에는 hasPath가 참이라
            //   재배치 블록을 건너뛰고, 바로 아래 ResetPath가 <b>방금 깐 그 경로를
            //   지웠기 때문이다.</b> 0.1초(=ATTACK_SCAN_INTERVAL)마다 깔았다 지웠다를
            //   반복하니 0.6m쯤 움직이다 멈추기를 되풀이했다.
            //   그래서 '지금 각을 잡으러 이동 중'을 상태로 들고, 도착할 때까지는
            //   아래 정지 코드로 내려가지 않는다.
            if (repositioning)
            {
                if (!ReachedDestination() && Time.time < repositionDeadline)
                    return true;

                repositioning = false;              //도착했거나 시간이 다 됐다 → 이제 겨눈다
                nextOrbitTime = Time.time + ORBIT_COOLDOWN;
            }

            if (Time.time >= nextOrbitTime
                && !HasPushOff(ai.transform.position, target.position)
                && TryOrbitForPushAngle(target))
            {
                repositioning = true;
                repositionDeadline = Time.time + REPOSITION_TIMEOUT;
                return true;
            }

            // ★ 쿨다운 동안은 서 있지 않는다
            //   어차피 못 치는 시간이므로 각을 따지지 않고 한 칸 돈다. 각이 나오는
            //   자리가 있으면 위에서 이미 잡았고, 여기는 '그래도 움직인다'가 목적이다.
            //   쿨다운이 풀리면 AttackReady가 참이 되어 이 분기를 빠져나가고,
            //   아래에서 멈춰 겨눈다 — 그래서 영원히 돌기만 하는 일은 없다.
            //   (여기엔 nextOrbitTime을 걸지 않는다. 그 쿨다운은 '치지 않고 돌기만
            //    하는 것'을 막으려는 건데, 지금은 애초에 칠 수 없는 시간이다)
            if (!ai.AttackReady && TryStrafeAroundTarget(target))
            {
                repositioning = true;
                repositionDeadline = Time.time + REPOSITION_TIMEOUT;
                return true;
            }

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

        //사거리를 벗어났다 → 겨누던 것도 각 잡던 것도 접는다
        aimStartTime = -1f;
        repositioning = false;

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

    /// <summary>
    /// 지정한 상대를 붙잡아 둔 채 옆 칸으로 옮긴다.
    ///
    /// ★ fromDanger를 받는 이유 — fleeing 플래그의 주인이 하나여야 한다
    ///   fleeing은 "발밑이 위험해서 나가는 중"이라는 뜻이고, Update의
    ///   `else if (fleeing)` 가지가 안전해진 순간 경로를 정리하는 데 쓴다.
    ///   그런데 교전 중 각을 잡으려는 재배치는 발밑이 멀쩡한 상태에서 부른다.
    ///   거기서 fleeing을 세우면 <b>바로 다음 틱에 그 가지가 경로를 지워</b>
    ///   봇이 한 걸음도 못 떼고 제자리에서 떨린다.
    ///   그래서 도주에서 부를 때만 세운다.
    /// </summary>
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

        //내가 선 칸만 예외로 두고 나머지 위험 칸은 피한다. 그런 길이 없으면 감수한다 —
        //발밑이 이미 닳은 상황이라 나가는 것 자체를 막으면 그 자리에서 같이 떨어진다
        if (!TrySetEscapePath(hit.position, allowDangerousCrossing: false)
            && !TrySetEscapePath(hit.position, allowDangerousCrossing: true))
            return false;

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

        //발판을 갈아타려고 옮기는 것이므로 '거기서 몇 걸음 더 버티나'가 가장 무겁다.
        //안 밟힌 칸 > 한 번 밟힌 칸 > 도착이 곧 붕괴인 칸 순으로 줄이 선다
        score += SurvivalBonusPerStep * StepsAfterArrivalOf(tileCenter);

        //이 칸에서 밀면 상대가 떨어진다면 그쪽으로 돌아가는 값이 있다
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

    // 도주 후보. 매 판단마다 새로 채우므로 배열 하나를 재사용한다
    private readonly Vector3[] escapeCandidates = new Vector3[3];
    private int escapeCandidateCount;

    private void AddEscapeCandidate(bool found, Vector3 pos)
    {
        if (found && escapeCandidateCount < escapeCandidates.Length)
            escapeCandidates[escapeCandidateCount++] = pos;
    }

    /// <summary>후보들을 순서대로 시도한다. 하나라도 경로가 깔리면 true.</summary>
    private bool TryEscapeToAny(bool allowDangerousCrossing)
    {
        for (int i = 0; i < escapeCandidateCount; i++)
        {
            if (NavMesh.SamplePosition(escapeCandidates[i], out NavMeshHit hit, 5f, ai.NavFilter)
                && TrySetEscapePath(hit.position, allowDangerousCrossing))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 완주 경로가 하나도 없을 때. 후보들을 순서대로 부분 경로로 시도하되
    /// <b>설 수 있는 칸에서 끝나는</b> 것을 고른다.
    ///
    /// ★ 예전엔 후보[0] 하나만 부분 경로로 시도했다
    ///   후보[0]은 FindEscapeTile — '위협에서 먼 곳'이라 맵 바깥이나 구멍 건너를
    ///   가리키기 쉽다. 거기로 부분 경로를 깔면 경로는 구멍 앞 <b>타일 가장자리</b>에서
    ///   끊기는데 바로 뒤에서 대쉬까지 쏘니, 봇은 멀쩡한 칸을 옆에 두고 꼭짓점으로
    ///   뛰어가 그 칸과 함께 떨어졌다. 나머지 후보(가장 가까운 안전 칸)는 보지도 못한 채였다.
    ///   위 두 단계는 후보를 전부 도는데 여기만 하나였다.
    /// </summary>
    private bool TryPartialEscapeToAny()
    {
        for (int i = 0; i < escapeCandidateCount; i++)
        {
            if (TrySetPartialPath(escapeCandidates[i]) && PathEndsOnFooting())
                return true;
        }

        //전부 가장자리에서 끊긴다면 그래도 움직인다 — 제자리가 확실한 죽음이다.
        //대쉬만 호출부가 막는다
        return escapeCandidateCount > 0 && TrySetPartialPath(escapeCandidates[0]);
    }

    /// <summary>지금 걸어둔 경로의 끝에 닿았는가.</summary>
    private bool ReachedDestination()
    {
        if (ai.Agent.pathPending)
            return false;
        if (!ai.Agent.hasPath)
            return true;

        return ai.Agent.remainingDistance <= ai.Agent.stoppingDistance + 0.5f;
    }

    //상대 주위를 몇 등분해서 훑을지. 12면 30°마다 본다
    private const int ORBIT_SAMPLES = 12;

    /// <summary>
    /// 교전 중 각 잡기 — <b>상대 주위를 배트 사거리로 돈다.</b>
    /// 각이 나오는 자리를 찾아 경로를 걸면 true.
    ///
    /// ★ 타일 중심을 후보로 쓰면 안 된다 — 처음에 그렇게 했다가 교전이 깨졌다
    ///   재배치 후보를 주변 타일의 중심으로 잡았는데, 타일이 14m 간격이고 배트
    ///   사거리는 4m(크기 2 기준)다. 격자에는 "상대에게서 4m 떨어진 칸 중심" 같은
    ///   자리가 사실상 없어서, 점수가 가장 나은 후보가 <b>28m 밖</b>인 일이 생겼다.
    ///   봇이 각을 잡겠다고 교전에서 통째로 걸어 나가 버린다.
    ///
    ///   각을 바꾸는 데 필요한 건 '어느 칸에 서느냐'가 아니라 '상대를 기준으로 어느
    ///   방향에 서느냐'다. 그래서 격자를 버리고 상대 중심의 원 위를 훑는다.
    ///   지금 서 있는 각도에서 좌우로 번갈아 넓혀 가며 보므로 <b>가장 조금 도는</b>
    ///   자리를 먼저 찾는다 — 한 칸(30°)이면 4m 반경에서 2m쯤 움직인다.
    /// </summary>
    private bool TryOrbitForPushAngle(Transform target)
    {
        return TryMoveOnOrbit(target, requirePushOff: true);
    }

    /// <summary>
    /// 각을 따지지 않고 상대 주위로 한 칸 돈다. <b>공격 쿨다운 동안</b> 쓴다.
    ///
    /// ★ 쿨다운 동안 서 있을 이유가 없다
    ///   사거리에 들어오면 ResetPath + velocity 0 으로 굳는데, 한 스윙 주기는
    ///   AIM_DELAY(0.45초) + BatCooldown(1.2초) ≈ 1.65초다. 그중 1.2초는 어차피
    ///   못 치는 시간인데 그동안 가만히 서 있었다. 봇이 여럿 붙어 있으면
    ///   화면의 정지 대부분이 여기서 나온다.
    ///   그 시간에 자리를 옮기면 (a) 움직이는 그림이 나오고 (b) 제자리 마모를
    ///   안 먹고 (c) 다음 스윙의 각이 좋아질 수도 있다.
    /// </summary>
    private bool TryStrafeAroundTarget(Transform target)
    {
        return TryMoveOnOrbit(target, requirePushOff: false);
    }

    private bool TryMoveOnOrbit(Transform target, bool requirePushOff)
    {
        var collapse = TileCollapseManager.Instance;
        var dm = DataManager.Instance;
        if (collapse == null || dm == null)
            return false;

        Vector3 targetPos = target.position;
        float standoff = dm.BatRange * ai.GetMyAuthorityScale();
        float knockback = KnockbackDistance();

        //지금 내가 서 있는 각도가 기준이다. 여기서 좌우로 벌려 나간다
        Vector3 toMe = ai.transform.position - targetPos;
        toMe.y = 0f;
        if (toMe.sqrMagnitude < 0.0001f)
            return false;

        float baseAngle = Mathf.Atan2(toMe.z, toMe.x);
        float stepAngle = Mathf.PI * 2f / ORBIT_SAMPLES;

        for (int step = 1; step <= ORBIT_SAMPLES / 2; step++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float a = baseAngle + sign * step * stepAngle;
                Vector3 spot = targetPos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * standoff;

                //이미 붕괴중인 자리는 못 쓴다. 그 밖은 아래 첫 유효 후보가 곧 가장 가까운 자리다
                if (collapse.StepsAfterArrival(spot) < 0)
                    continue;

                if (requirePushOff && !collapse.HasPushOff(spot, targetPos, knockback))
                    continue;

                if (!NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, ai.NavFilter))
                    continue;

                // ★ 여기는 TrySetSafePath를 써야 한다 — 처음엔 날것으로 SetPath 했고 그게 버그였다
                //   도주 분기가 위험 검사를 건너뛰는 건 <b>이미 꺼지는 발판 위에 서 있어서</b>
                //   나가는 길을 막으면 그 자리에서 같이 떨어지기 때문이다. 그건 응급이다.
                //   각 잡기는 응급이 아니다. 발밑이 멀쩡한 상태에서 더 좋은 자리를 찾아가는
                //   것뿐인데, 검사를 건너뛰면 곧 꺼질 타일을 가로질러 간다.
                //
                //   하필 이 이동은 <b>일부러 구멍 근처로</b> 간다(각이 나오는 자리를 찾으므로).
                //   가장 위험한 경로에만 검사가 빠져 있던 셈이다.
                //   타일은 붕괴가 시작되고도 2.0초(collapseDelay) 동안 바닥이 남아 있고
                //   NavMesh는 그 뒤에야 잘리므로, 그 창에서는 검사만이 유일한 방어다.
                ai.ApplyStateSpeed();
                if (TrySetSafePath(hit.position))
                    return true;
            }
        }

        return false;
    }

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

        // ★ 도착하기 <b>전에</b> 다음 목적지를 잡는다 — 그래야 안 멈춘다
        //   예전엔 남은 거리가 stoppingDistance + 0.5m(=0.8m) 아래로 떨어져야 다음
        //   목적지를 골랐다. 그런데 에이전트는 stoppingDistance 근처에서 이미 감속해
        //   거의 서고, 그 뒤에 새 경로가 깔린다. 목적지마다 한 번씩 서는 셈이라
        //   봇이 '가다 서다'를 반복하는 것처럼 보였다.
        //   조금 남았을 때 미리 다음 다리를 이어 붙이면 속도를 잃지 않는다.
        if (ai.Agent.hasPath && ai.Agent.remainingDistance > WANDER_RETARGET_DISTANCE)
            return;

        ai.ApplyStateSpeed();

        if (ai.TryGetWanderDestination(out Vector3 dest)
            && NavMesh.SamplePosition(dest, out NavMeshHit hit, 5f, ai.NavFilter)
            && TrySetSafePath(hit.position))
            return;

        // ★ 배회 경로가 거부되면 <b>그냥 서 있었다</b> — 이 모드에서 그건 죽음이다
        //   후반엔 닳은 칸이 많아 TrySetSafePath 거부가 잦다. 거부될 때마다 봇은 섰고,
        //   서 있으면 제자리 마모가 2초마다 자기 발밑을 깎는다. 그러다 '발밑 위험'이
        //   뜨면 도주 분기가 <b>타일 정중앙</b>(CellCenter)으로 옮겨 주고, 거기서 또 선다.
        //   "타일 중앙에 섰다가 빨개지면 다음 타일 중앙으로"의 정체가 이 순환이다.
        //
        //   갈 곳을 못 찾으면 옆의 멀쩡한 칸으로라도 옮긴다. 어디로든 옮기면
        //   그 칸의 제자리 마모 시계가 처음부터 다시 간다.
        var collapse = TileCollapseManager.Instance;
        if (collapse == null)
            return;

        wanderFromPos = ai.transform.position;
        wanderScorer ??= ScoreWanderFallbackTile;

        if (collapse.FindBestFooting(wanderFromPos, WANDER_FALLBACK_CELLS, wanderScorer, out Vector3 spot)
            && NavMesh.SamplePosition(spot, out NavMeshHit fallbackHit, 5f, ai.NavFilter))
            TrySetEscapePath(fallbackHit.position, allowDangerousCrossing: false);
    }

    //남은 거리가 이보다 짧아지면 다음 목적지를 미리 잡는다. 감속 구간보다 넉넉해야 한다
    private const float WANDER_RETARGET_DISTANCE = 3f;

    //배회 폴백에서 훑을 반경. 한 칸 옆이면 충분하다 — 멀리 갈 이유가 없다
    private const int WANDER_FALLBACK_CELLS = 1;

    private Vector3 wanderFromPos;
    private System.Func<Vector3, float> wanderScorer;

    //가까운 칸일수록 좋다. 목적은 '다른 칸으로 옮기는 것' 자체다
    private float ScoreWanderFallbackTile(Vector3 tileCenter)
    {
        return SurvivalBonusPerStep * StepsAfterArrivalOf(tileCenter)
             - Vector3.Distance(tileCenter, wanderFromPos);
    }

    //"도착 후 한 걸음 더 버티는 것"이 "1m 더 가는 것"의 몇 배 가치인가.
    //타일이 14m라 그보다 크게 둬야 거리 항에 묻히지 않는다
    private const float SurvivalBonusPerStep = 40f;

    private static int StepsAfterArrivalOf(Vector3 tileCenter)
    {
        var collapse = TileCollapseManager.Instance;
        return collapse != null ? Mathf.Max(0, collapse.StepsAfterArrival(tileCenter)) : 0;
    }

    // ═════════════════════════════════════════════════════════
    //  ★ 대상 고르기 — 크기 필터를 점수로 바꿨다
    // ═════════════════════════════════════════════════════════
    //
    //  예전엔 이 한 줄이 전부였다:
    //      if (e.IsBot && e.ScaleValue >= myScale) continue;
    //  자기보다 <b>엄격히 작은</b> 봇만 대상으로 봤다는 뜻이다. 그런데 봇은 전원
    //  같은 크기로 태어나고(LanBotSpawner는 위치만 흩뿌린다), 밀치기 모드에서
    //  크기가 느는 유일한 길은 누군가를 때리는 것(GrowKind.BatHit)이다.
    //  그래서 순환이 닫혀 있었다 — 봇을 때리려면 먼저 커야 하고, 커지려면 때려야
    //  하는데, 때릴 수 있는 건 크기 조건이 없는 사람뿐이다.
    //  결과적으로 <b>봇끼리는 한 번도 싸우지 않았다.</b>
    //
    //  그 필터가 원래 막으려던 것은 크기가 아니라 <b>몰림</b>이었다. 봇들이 서로를
    //  쫓다 한 칸에 뭉치면 그 칸이 마모로 꺼져 다 같이 떨어졌다. 그건 진짜 문제였다.
    //  이제 몰림을 몰림으로 센다 — 같은 상대를 노리는 봇 수가 감점이다.
    //  크기는 조건이 아니라 가벼운 가중치 하나로만 남는다.

    //이미 그 상대를 노리는 봇 하나당 감점. 몰림을 막는 항이다
    private const float ClaimPenalty = 12f;

    //나보다 작은 상대에 주는 가점. 밀어내기 쉬우니 조금 선호할 뿐, 조건은 아니다
    private const float SmallerBonus = 4f;

    /// <summary>
    /// 지금 노릴 만한 상대. 없으면 null.
    /// 고른 결과를 ai.PushTarget에 남겨, 다른 봇이 몰림을 셀 수 있게 한다.
    /// </summary>
    private Transform FindNearestTarget()
    {
        Vector3 myPos = ai.transform.position;
        float myScale = ai.GetMyAuthorityScale();

        float bestScore = float.MinValue;
        Transform best = null;

        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null || e.Transform == ai.transform)
                continue;
            if (e.IsOutOfPlay)
                continue; // 탈락/흡수 판정 단일 출처 (G6/K2)

            float distance = Vector3.Distance(myPos, e.Transform.position);
            if (distance >= ai.DetectRadius)
                continue;

            //가까울수록 좋다가 기본이고, 나머지 항이 그걸 흔든다
            float score = -distance;

            //여기서 밀면 떨어뜨릴 수 있다면 그게 이 모드의 목적이다 — 가장 큰 항
            if (HasPushOff(myPos, e.Transform.position))
                score += PushOffBonus;

            score -= ClaimPenalty * CountClaims(e.Transform);

            if (e.ScaleValue < myScale)
                score += SmallerBonus;

            if (score > bestScore)
            {
                bestScore = score;
                best = e.Transform;
            }
        }

        ai.PushTarget = best;
        return best;
    }

    /// <summary>이 상대를 이미 노리고 있는 <b>다른</b> 봇의 수.</summary>
    private int CountClaims(Transform target)
    {
        int n = 0;

        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Identity == null)
                continue;

            AIPlayerMovement other = e.Identity.Bot;
            if (other == null || other == ai)
                continue;

            if (other.PushTarget == target)
                n++;
        }

        return n;
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
