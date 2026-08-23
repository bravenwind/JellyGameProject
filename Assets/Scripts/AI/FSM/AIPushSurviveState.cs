using UnityEngine;
using UnityEngine.AI;

public class AIPushSurviveState : AIBaseState
{
    private float _checkTimer;
    private bool _fleeing;
    private float _attackScanTimer;

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
        _checkTimer = 0f;
        _fleeing = false;
        _attackScanTimer = 0f;
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0.3f;
        ai.Agent.ResetPath();
        ai.Agent.velocity = Vector3.zero;
    }

    public override void Update()
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh) return;
        if (ai.IsDashing || ai.IsAttacking) return;

        _checkTimer += Time.deltaTime;
        if (_checkTimer < CHECK_INTERVAL) return;

        //이번 판단이 실제로 몇 초 만에 돌아왔는지. 아래 공격 스캔 주기가 이 값을 쓴다.
        //예전엔 상수 CHECK_INTERVAL을 그대로 더해서, 프레임이 길거나 도주 분기로
        //빠진 틱이 있으면 공격 스캔 간격이 실제 시간과 조용히 어긋났다
        float sinceLastCheck = _checkTimer;
        _checkTimer = 0f;

        var collapse = TileCollapseManager.Instance;
        if (collapse == null) return;

        bool onDanger = collapse.IsPositionDangerous(ai.transform.position);

        if (onDanger)
        {
            // ★ 도망치면서도 사거리 안이면 친다.
            //
            //   예전엔 위험 타일 위에 있으면 <b>도망만</b> 갔다. 그런데 밀치기 모드는
            //   시간이 갈수록 발판이 닳아 '위험한 칸'이 늘어난다. 결국 봇은 판 내내
            //   도망만 다니고 한 번도 공격하지 않는다.
            //   배트를 휘두르는 건 제자리 동작이라 도피와 동시에 할 수 있다.
            TryOpportunisticSwing();

            // ★ 도망갈 곳은 '가까운 안전 타일'이 아니라 '위협에서 먼 안전 타일'이다.
            //   가까운 곳만 찾으면 무너지는 발판은 피했는데 때리려는 사람 품으로 뛰어든다.
            Vector3 threat = NearestThreatPos();

            Vector3 safePos;
            bool ok = collapse.FindEscapeTile(ai.transform.position, threat, out safePos);

            // 그런 곳이 없으면(사방이 위험) 최소한 발판이 남은 곳으로
            if (!ok && !collapse.FindNearestSafeTile(ai.transform.position, out safePos, avoidDangerous: true))
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
                    _fleeing = true;
                    ai.TryDash();
                }
            }
        }
        else if (_fleeing)
        {
            ai.Agent.ResetPath();
            ai.Agent.velocity = Vector3.zero;
            ai.Agent.speed = ai.moveSpeed;
            _fleeing = false;
        }
        else
        {
            UpdateCombatOrWander(sinceLastCheck);
        }
    }

    /// <summary>
    /// 안전한 상태일 때의 행동. 근처에 타겟이 있으면 추격/공격, 없으면 위험 타일을
    /// 피해 평소처럼 배회(roam)한다.
    /// </summary>
    private void UpdateCombatOrWander(float deltaSinceLastCheck)
    {
        _attackScanTimer += deltaSinceLastCheck;
        if (_attackScanTimer < ATTACK_SCAN_INTERVAL) return;
        _attackScanTimer = 0f;

        Transform target = FindNearestTarget();
        if (target != null && TryEngageTarget(target))
            return;

        // 타겟이 없거나 추격 범위 밖 → 평소엔 배회
        Wander();
    }

    /// <summary>타겟을 추격하거나 사거리 안이면 공격. 무언가 행동했으면 true.</summary>
    private bool TryEngageTarget(Transform target)
    {
        Vector3 dirToTarget = target.position - ai.transform.position;
        dirToTarget.y = 0;
        float dist = dirToTarget.magnitude;

        var dm = DataManager.Instance;
        float range = dm != null ? dm.batRange * ai.GetMyAuthorityScale() : 2f;

        // ── 사거리 안: 돌아서서 친다 ──
        //
        //   ★ 경로를 먼저 지운다.
        //     경로가 남아 있으면 AIPlayerMovement.Update가 매 프레임
        //     "가려는 방향"으로 회전을 덮어쓴다. 그러면 FaceTarget으로 맞춰둔 각도가
        //     바로 풀려 배트 판정(전방 ±60°)을 빗나간다.
        //     휘두르는 동안은 제자리에서 상대를 보고 있어야 한다.
        if (dist <= range * 1.2f)
        {
            if (ai.Agent.enabled && ai.Agent.isOnNavMesh && ai.Agent.hasPath)
                ai.Agent.ResetPath();
            ai.Agent.velocity = Vector3.zero;

            FaceTarget(dirToTarget);
            ai.TryAttack();
            return true;
        }

        // ── 추격 ──
        //
        //   ★ 추격 범위를 감지 반경 전체로 넓혔다.
        //     예전엔 detectRadius * 0.6 안에 들어와야 쫓았다. 그 밖이면 상대를
        //     <b>보고 있으면서도 딴 데로 배회</b>했다. 눈앞에서 어슬렁거리는 것처럼 보인다.
        //
        //   ★ 목표 지점은 '지금 위치'가 아니라 '갈 곳'으로 잡는다.
        //     상대가 움직이는 동안 현재 위치로만 경로를 잡으면 계속 뒤를 따라간다.
        //     조금 앞을 노리면 실제로 따라잡는다.
        if (dist < ai.detectRadius)
        {
            Vector3 aim = target.position + PredictLead(target, dist);

            if (NavMesh.SamplePosition(aim, out NavMeshHit hit, 5f, ai.NavFilter)
                || NavMesh.SamplePosition(target.position, out hit, 5f, ai.NavFilter))
            {
                ai.Agent.speed = ai.moveSpeed;

                if (TrySetSafePath(hit.position))
                {
                    // 거리가 꽤 벌어져 있으면 대쉬로 붙는다(원래 도주에만 쓰던 것을 공격에도).
                    if (dist > range * 3f) ai.TryDash();
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
        if (ai.moveSpeed <= 0.01f) return Vector3.zero;

        Vector3 vel = Vector3.zero;

        CharacterController cc = target.GetComponentInChildren<CharacterController>();
        if (cc != null && cc.enabled) 
            vel = cc.velocity;
        else
        {
            NavMeshAgent ag = target.GetComponentInChildren<NavMeshAgent>();
            if (ag != null && ag.enabled) vel = ag.velocity;
        }

        vel.y = 0f;
        if (vel.sqrMagnitude < 0.01f) return Vector3.zero;

        float travelTime = Mathf.Clamp(dist / ai.moveSpeed, 0f, 0.7f);
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

        foreach (var p in EntityRegistry.Players)
        {
            if (p == null || p.IsOutOfPlay) continue;
            float d = Vector3.Distance(ai.transform.position, p.transform.position);
            if (d < best) { best = d; pos = p.transform.position; }
        }

        foreach (var b in EntityRegistry.Bots)
        {
            if (b == null || b == ai || b.IsOutOfPlay) continue;
            float d = Vector3.Distance(ai.transform.position, b.transform.position);
            if (d < best) { best = d; pos = b.transform.position; }
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
        if (dm == null) return;

        float range = dm.batRange * ai.GetMyAuthorityScale();

        Transform target = FindNearestTarget();
        if (target == null) return;

        Vector3 d = target.position - ai.transform.position;
        d.y = 0f;
        if (d.magnitude > range * 1.2f) return;

        FaceTarget(d);
        ai.TryAttack();
    }

    /// <summary>위험 타일을 피해 무작위 안전 지점으로 배회한다.</summary>
    private void Wander()
    {
        // 이미 경로를 따라 이동 중이면 목적지 도착/경로 소실 전까진 그대로 둔다.
        if (ai.Agent.pathPending) return;
        if (ai.Agent.hasPath && ai.Agent.remainingDistance > ai.Agent.stoppingDistance + 0.5f)
            return;

        ai.Agent.speed = ai.moveSpeed;

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

        foreach (var player in EntityRegistry.Players)
        {
            if (player == null || player.IsOutOfPlay) continue; // 탈락/흡수 판정 단일 출처 (G6/K2)

            // ★ 사람은 크기와 무관하게 대상이다.
            //
            //   밀치기 모드에는 <b>잡아먹히는 개념이 없다.</b> 큰 상대라고 피할 이유가 없고,
            //   오히려 큰 상대일수록 밀어 떨어뜨릴 가치가 있다.
            //
            //   예전엔 크기로 걸렀는데 그게 이런 악순환을 만들었다:
            //     플레이어가 배트를 맞힌다 → 커진다 → 봇의 대상에서 빠진다
            //     → 봇이 공격을 못 한다 → 봇은 안 커진다 → 격차가 더 벌어진다
            //   한 번 맞기 시작하면 봇이 영원히 반격하지 못하는 구조였다.
            //
            //   크기 조건은 아래 '봇끼리' 순회에만 남긴다 — 그건 서로 추격하며
            //   한 타일에 뭉치는 것을 막기 위한 별개의 규칙이다.

            float d = Vector3.Distance(ai.transform.position, player.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = player.transform;
            }
        }

        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || bot == ai || bot.IsEliminated || bot.IsBeingAbsorbed) continue;
            if (bot.GetMyAuthorityScale() >= myScale) continue; // 동급/대형 봇은 추격 안 함(상호 추격 차단)
            float d = Vector3.Distance(ai.transform.position, bot.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = bot.transform;
            }
        }

        return bestDist < ai.detectRadius ? best : null;
    }

    private void FaceTarget(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.01f) return;
        ai.transform.rotation = Quaternion.LookRotation(direction.normalized);
    }

    public override void Exit()
    {
        ai.Agent.speed = ai.moveSpeed;
        ai.Agent.stoppingDistance = 0f;
    }
}
