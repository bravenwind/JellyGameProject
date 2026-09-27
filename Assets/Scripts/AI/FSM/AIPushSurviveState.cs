using UnityEngine;
using UnityEngine.AI;
using JellyNet;

public class AIPushSurviveState : AIBaseState
{
    private float checkTimer;
    private bool fleeing;
    private float attackScanTimer;

    private bool repositioning;

    private float nextOrbitTime;

    private float repositionDeadline;

    private const float ORBIT_COOLDOWN = 1.5f;

    private const float REPOSITION_TIMEOUT = 2.5f;

    private const float CHECK_INTERVAL = 0.06f;
    private const float ATTACK_SCAN_INTERVAL = 0.1f;

    private const float AIM_DELAY = 0.1f;

    private float aimStartTime = -1f;

    private const int REPOSITION_SEARCH_CELLS = 2;

    private Vector3 repositionTargetPos;
    private float repositionPreferredDist;
    private Vector3 repositionFromPos;
    private System.Func<Vector3, float> repositionScorer;

    private const float PushOffBonus = 25f;

    private const float TravelCostWeight = 0.6f;

    private readonly Vector3[] escapeCandidates = new Vector3[3];
    private int escapeCandidateCount;

    private const int ORBIT_SAMPLES = 12;

    private const float WANDER_RETARGET_DISTANCE = 3f;

    private const int WANDER_FALLBACK_CELLS = 1;

    private Vector3 wanderFromPos;
    private System.Func<Vector3, float> wanderScorer;

    private const float SurvivalBonusPerStep = 40f;

    private const float ClaimPenalty = 12f;

    private const float SmallerBonus = 4f;

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

        if (HandleStuck())
        {
            repositioning = false;
            return;
        }

        checkTimer += Time.deltaTime;
        if (checkTimer < CHECK_INTERVAL)
            return;

        float sinceLastCheck = checkTimer;
        checkTimer = 0f;

        var collapse = TileCollapseManager.Instance;
        if (collapse == null)
            return;

        bool onDanger = collapse.IsFootingUnsafe(ai.transform.position);

        if (onDanger)
        {
            TryOpportunisticSwing();

            if (fleeing && ai.Agent.hasPath && !ReachedDestination()
                && collapse.StepsAfterArrival(ai.Agent.destination) >= 0)
                return;

            if (TryReposition())
                return;

            Vector3 threat = NearestThreatPos();

            escapeCandidateCount = 0;
            AddEscapeCandidate(collapse.FindEscapeTile(ai.transform.position, threat, out Vector3 a), a);
            AddEscapeCandidate(collapse.FindNearestSafeTile(ai.transform.position, out Vector3 b, avoidDangerous: true), b);
            AddEscapeCandidate(collapse.FindNearestSafeTile(ai.transform.position, out Vector3 c, avoidDangerous: false), c);

            if (escapeCandidateCount == 0)
                return;

            if (TryEscapeToAny(allowDangerousCrossing: false)
                || TryEscapeToAny(allowDangerousCrossing: true)
                || TryPartialEscapeToAny())
            {
                fleeing = true;

                if (PathEndsOnFooting())
                    ai.TryDash();
            }
        }
        else if (fleeing)
        {
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

    private void UpdateCombatOrWander(float deltaSinceLastCheck)
    {
        attackScanTimer += deltaSinceLastCheck;
        if (attackScanTimer < ATTACK_SCAN_INTERVAL)
            return;
        attackScanTimer = 0f;

        Transform target = FindNearestTarget();
        if (target != null && TryEngageTarget(target))
            return;

        Wander();
    }

    private bool TryEngageTarget(Transform target)
    {
        Vector3 dirToTarget = target.position - ai.transform.position;
        dirToTarget.y = 0;
        float dist = dirToTarget.magnitude;

        var dm = DataManager.Instance;
        float range = dm != null ? dm.BatRange * ai.GetMyAuthorityScale() : 2f;

        if (dist <= range * 1.2f)
        {
            if (repositioning)
            {
                if (!ReachedDestination() && Time.time < repositionDeadline)
                    return true;

                repositioning = false;
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

            if (!ai.AttackReady && TryStrafeAroundTarget(target))
            {
                repositioning = true;
                repositionDeadline = Time.time + REPOSITION_TIMEOUT;
                return true;
            }

            if (ai.Agent.enabled && ai.Agent.isOnNavMesh && ai.Agent.hasPath)
                ai.Agent.ResetPath();
            ai.Agent.velocity = Vector3.zero;

            FaceTarget(dirToTarget);

            if (aimStartTime < 0f)
                aimStartTime = Time.time;

            if (Time.time - aimStartTime >= AIM_DELAY)
            {
                ai.TryAttack();
                aimStartTime = -1f;
            }

            return true;
        }

        aimStartTime = -1f;
        repositioning = false;

        if (dist < ai.DetectRadius)
        {
            Vector3 aim = target.position + PredictLead(target, dist);

            if (NavMesh.SamplePosition(aim, out NavMeshHit hit, 5f, ai.NavFilter)
                || NavMesh.SamplePosition(target.position, out hit, 5f, ai.NavFilter))
            {
                ai.ApplyStateSpeed();

                if (TrySetSafePath(hit.position))
                {
                    if (dist > range * 3f)
                        ai.TryDash();
                    return true;
                }
            }
        }

        return false;
    }

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

        if (aimStartTime < 0f)
            aimStartTime = Time.time;

        if (Time.time - aimStartTime < AIM_DELAY)
            return;

        ai.TryAttack();
        aimStartTime = -1f;
    }

    private bool TryReposition()
    {
        Transform target = FindNearestTarget();
        return target != null && TryRepositionFor(target);
    }

    private bool TryRepositionFor(Transform target)
    {
        var collapse = TileCollapseManager.Instance;
        var dm = DataManager.Instance;
        if (collapse == null || dm == null)
            return false;

        repositionTargetPos = target.position;
        repositionFromPos = ai.transform.position;

        repositionPreferredDist = dm.BatRange * ai.GetMyAuthorityScale();

        repositionScorer ??= ScoreRepositionTile;

        if (!collapse.FindBestFooting(repositionFromPos, REPOSITION_SEARCH_CELLS,
                                      repositionScorer, out Vector3 spot))
            return false;

        if (!NavMesh.SamplePosition(spot, out NavMeshHit hit, 5f, ai.NavFilter))
            return false;

        if (!TrySetEscapePath(hit.position, allowDangerousCrossing: false)
            && !TrySetEscapePath(hit.position, allowDangerousCrossing: true))
            return false;

        ai.ApplyStateSpeed();
        fleeing = true;
        return true;
    }

    private float ScoreRepositionTile(Vector3 tileCenter)
    {
        float distToTarget = Vector3.Distance(tileCenter, repositionTargetPos);
        float travel = Vector3.Distance(tileCenter, repositionFromPos);

        float score = -Mathf.Abs(distToTarget - repositionPreferredDist) - travel * TravelCostWeight;

        score += SurvivalBonusPerStep * StepsAfterArrivalOf(tileCenter);

        if (HasPushOff(tileCenter, repositionTargetPos))
            score += PushOffBonus;

        return score;
    }

private void AddEscapeCandidate(bool found, Vector3 pos)
    {
        if (found && escapeCandidateCount < escapeCandidates.Length)
            escapeCandidates[escapeCandidateCount++] = pos;
    }

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

    private bool TryPartialEscapeToAny()
    {
        for (int i = 0; i < escapeCandidateCount; i++)
        {
            if (TrySetPartialPath(escapeCandidates[i]) && PathEndsOnFooting())
                return true;
        }

        return escapeCandidateCount > 0 && TrySetPartialPath(escapeCandidates[0]);
    }

    private bool ReachedDestination()
    {
        if (ai.Agent.pathPending)
            return false;
        if (!ai.Agent.hasPath)
            return true;

        return ai.Agent.remainingDistance <= ai.Agent.stoppingDistance + 0.5f;
    }

    private bool TryOrbitForPushAngle(Transform target)
    {
        return TryMoveOnOrbit(target, requirePushOff: true);
    }

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

                if (collapse.StepsAfterArrival(spot) < 0)
                    continue;

                if (requirePushOff && !collapse.HasPushOff(spot, targetPos, knockback))
                    continue;

                if (!NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, ai.NavFilter))
                    continue;

                ai.ApplyStateSpeed();
                if (TrySetSafePath(hit.position))
                    return true;
            }
        }

        return false;
    }

    private float KnockbackDistance()
    {
        var dm = DataManager.Instance;
        if (dm == null)
            return 0f;

        float force = dm.BatPushForce
                    * (ai.GetMyAuthorityScale() / Mathf.Max(0.01f, NetEntity.BaselineScale));

        return force * Knockback.DURATION * 0.5f;
    }

    private bool HasPushOff(Vector3 from, Vector3 targetPos)
    {
        var collapse = TileCollapseManager.Instance;
        return collapse != null && collapse.HasPushOff(from, targetPos, KnockbackDistance());
    }

    private void Wander()
    {
        if (ai.Agent.pathPending)
            return;

        if (ai.Agent.hasPath && ai.Agent.remainingDistance > WANDER_RETARGET_DISTANCE)
            return;

        ai.ApplyStateSpeed();

        if (ai.TryGetWanderDestination(out Vector3 dest)
            && NavMesh.SamplePosition(dest, out NavMeshHit hit, 5f, ai.NavFilter)
            && TrySetSafePath(hit.position))
            return;

        var collapse = TileCollapseManager.Instance;
        if (collapse == null)
            return;

        wanderFromPos = ai.transform.position;
        wanderScorer ??= ScoreWanderFallbackTile;

        if (collapse.FindBestFooting(wanderFromPos, WANDER_FALLBACK_CELLS, wanderScorer, out Vector3 spot)
            && NavMesh.SamplePosition(spot, out NavMeshHit fallbackHit, 5f, ai.NavFilter))
            TrySetEscapePath(fallbackHit.position, allowDangerousCrossing: false);
    }

    private float ScoreWanderFallbackTile(Vector3 tileCenter)
    {
        return SurvivalBonusPerStep * StepsAfterArrivalOf(tileCenter)
             - Vector3.Distance(tileCenter, wanderFromPos);
    }

    private static int StepsAfterArrivalOf(Vector3 tileCenter)
    {
        var collapse = TileCollapseManager.Instance;
        return collapse != null ? Mathf.Max(0, collapse.StepsAfterArrival(tileCenter)) : 0;
    }

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
                continue;

            float distance = Vector3.Distance(myPos, e.Transform.position);
            if (distance >= ai.DetectRadius)
                continue;

            float score = -distance;

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
