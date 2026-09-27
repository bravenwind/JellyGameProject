using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using JellyNet;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AIDetector))]
public class AIPlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;

    [SerializeField] private float rotateSpeed = 10f;

    [SerializeField] private bool matchPlayerSpeed = true;

    [SerializeField] private float speedRatio = 1f;

    [SerializeField] private float detectRadius = 15f;

    [SerializeField] private float stateEvalRate = 0.15f;

    public float BaseAgentRadius { get; private set; }
    public float BaseAgentHeight { get; private set; }

    public Transform PushTarget { get; set; }

    [SerializeField] private Transform batPivot;

    [SerializeField] private bool hideBatWhenIdle = true;

    [SerializeField] private float dashSpeed = 80f;

    [SerializeField] private float dashDuration = 0.2f;

    [SerializeField] private float dashCooldown = 3f;

    [SerializeField] private NameTagBillboard nameTagBillboard;

    public NavMeshAgent Agent { get; private set; }
    public PlayerScaleController ScaleCtrl { get; private set; }
    public NavMeshQueryFilter NavFilter { get; private set; }
    public NavMeshPath CachedPath { get; private set; }
    public AIDetector Detector { get; private set; }

    private Animator anim;
    private LanPlayerVisual visual;

    private AIBaseState currentState;
    private bool isTransitioning = false;

    public AIWanderState WanderState { get; private set; }
    public AIChaseState  ChaseState  { get; private set; }
    public AIFleeState   FleeState   { get; private set; }
    public AIPushSurviveState PushSurviveState { get; private set; }

    private float lastUrgentThreatCheck;
    public bool IsBeingAbsorbed { get; set; } = false;
    public bool IsEliminated { get; private set; } = false;

    private float dashCooldownTimer;
    private float dashTimer;

    private float stateSpeedRatio = 1f;

    private float attackCooldownTimer;
    private Coroutine attackCoroutine;

    private NetIdentity netId;
    private LanBotState botSync;

    private const float FallbackScatterRadius = 4f;

    private const int GroundCheckInterval = 15;

    private const float GroundRayLift = 0.5f;    // 피벗에서 이만큼 위에서 쏨
    private const float GroundRayReach = 2.5f;    // 발바닥 아래로 이만큼까지 땅을 찾음

    private Collider bodyCollider;

    private const int BasePriority = 50;    // 시작 크기(1배)일 때 값
    private const float PriorityPerScale = 10f;    // 1배 커질 때마다 빼는 값

    private int avoidanceJitter;

    private Coroutine knockbackCoroutine;

    public float MoveSpeed { get { return moveSpeed; } set { moveSpeed = value; } }

    public float DetectRadius { get { return detectRadius; } }
    public Transform BatPivot { get { return batPivot; } }
    public bool HideBatWhenIdle { get { return hideBatWhenIdle; } }

    public bool IsOutOfPlay => IsEliminated || IsBeingAbsorbed;
    public bool IsDashing => dashTimer > 0f;
    public bool IsAttacking => attackCoroutine != null;

    public bool AttackReady => attackCoroutine == null && attackCooldownTimer <= 0f;

    private bool IsDriver
    {
        get
        {
            return netId != null && netId.IsMineOrOffline;
        }
    }

    public float GetMyAuthorityScale()
    {
        return ScaleCtrl != null ? ScaleCtrl.CurrentScaleValue : transform.localScale.x;
    }

    private void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        avoidanceJitter = Random.Range(-10, 11);
        ScaleCtrl = GetComponent<PlayerScaleController>();
        Detector = GetComponent<AIDetector>();
        netId = GetComponent<NetIdentity>();
        botSync = GetComponent<LanBotState>();
        CachedPath = new NavMeshPath();
        anim = GetComponentInChildren<Animator>();
        visual = GetComponentInParent<LanPlayerVisual>();

        BaseAgentRadius = NavMeshUtil.AgentRadius(Agent.agentTypeID);
        BaseAgentHeight = NavMeshUtil.AgentHeight(Agent.agentTypeID);

        Agent.radius = BaseAgentRadius;
        Agent.height = BaseAgentHeight;

        Detector.Configure(detectRadius, BaseAgentRadius);

        ApplyPlayerSpeed();

        ApplyStateSpeed();
        Agent.acceleration = 1000f;
        Agent.angularSpeed = 0f;
        Agent.stoppingDistance = 0f;
        Agent.autoBraking = false;
    }

    private void OnEnable()
    {
        if (ScaleCtrl != null)
            ScaleCtrl.OnScalePhysicsRebuilt += UpdateScaleOnAgent;
    }

    private void OnDisable()
    {
        if (ScaleCtrl != null)
            ScaleCtrl.OnScalePhysicsRebuilt -= UpdateScaleOnAgent;
    }

    private void Start()
    {
        WanderState = new AIWanderState(this);
        ChaseState  = new AIChaseState(this);
        FleeState   = new AIFleeState(this);
        PushSurviveState = new AIPushSurviveState(this);

        ApplyBatModeVisibility();

        if (!IsDriver)
        {
            PlayerAbsorber absorber = GetComponent<PlayerAbsorber>();
            if (absorber != null)
                absorber.enabled = false;
            SoftBody3D softBody = GetComponentInChildren<SoftBody3D>();
            if (softBody != null)
                softBody.RemoveCloth();

            Agent.enabled = false;
            return;
        }

        InitAndStart();
    }

    private void InitAndStart()
    {
        Agent.enabled = false;

        NavFilter = new NavMeshQueryFilter
        {
            agentTypeID = Agent.agentTypeID,
            areaMask    = NavMeshUtil.WalkableMask
        };

        if (!TryPlaceOnNavMesh())
            return;

        Agent.enabled = true;

        if (!Agent.isOnNavMesh)
            return;

        ApplyAvoidancePriority(GetMyAuthorityScale());

        ChangeState(GameState.CurrentGameMode == GameModeType.Push ? PushSurviveState : WanderState);
        StartCoroutine(StateEvalLoop());
    }

    private bool TryPlaceOnNavMesh()
    {
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavFilter))
        {
            transform.position = hit.position;
            return true;
        }

        if (!TryFindFallbackSpawnPos(out Vector3 fallback))
            return false;

        if (!NavMesh.SamplePosition(fallback, out NavMeshHit fhit, 10f, NavFilter))
            return false;

        transform.position = fhit.position;
        return true;
    }

    public void ChangeState(AIBaseState newState)
    {
        if (currentState == newState)
            return;
        if (isTransitioning)
            return;

        isTransitioning = true;
        currentState?.Exit();
        currentState = newState;
        currentState?.Enter();
        isTransitioning = false;
    }

    private IEnumerator StateEvalLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(stateEvalRate);

            if (!Agent.enabled)
                continue;

            if (!Agent.isOnNavMesh)
            {
                if (CheckGroundBelow(true))
                    continue;

                var c = TileCollapseManager.Instance;
                if (c != null && c.FindNearestSafeTile(transform.position, out Vector3 offSafe)
                    && NavMesh.SamplePosition(offSafe, out NavMeshHit offHit, 10f, NavFilter))
                {
                    RecoverTo(offHit.position);
                    continue;
                }

                if (NavMesh.SamplePosition(transform.position, out NavMeshHit nearHit, 3f, NavFilter))
                {
                    RecoverTo(nearHit.position);
                    continue;
                }

                AwakeFallPhysics();
                continue;
            }

            var collapse = TileCollapseManager.Instance;
            if (collapse != null && collapse.IsOverVoid(transform.position))
            {
                if (CheckGroundBelow(true))
                    continue;

                if (collapse.FindNearestSafeTile(transform.position, out Vector3 safeTile)
                    && (NavMesh.SamplePosition(safeTile, out NavMeshHit voidHit, 10f, NavFilter)))
                {
                    Agent.Warp(voidHit.position);
                    if (Agent.isOnNavMesh && Agent.hasPath)
                        Agent.ResetPath();
                }
                continue;
            }

            if (GameState.CurrentGameMode == GameModeType.Push)
            {
                ChangeState(PushSurviveState);
                continue;
            }

            if (collapse != null && collapse.IsFootingUnsafe(transform.position))
            {
                if (TryGetWanderDestination(out Vector3 safe))
                    Agent.SetDestination(safe);
                ChangeState(WanderState);
                TryDash();
                continue;
            }

            EvaluateAndTransition();
        }
    }

    public void EvaluateAndTransition()
    {
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            ChangeState(PushSurviveState);
            return;
        }

        if (Detector.FindThreat() != null)
        {
            ChangeState(FleeState);
            return;
        }

        if (Detector.FindTargetToChase() != null)
        {
            ChangeState(ChaseState);
            return;
        }
        ChangeState(WanderState);
    }

    private void ApplyPlayerSpeed()
    {
        if (!matchPlayerSpeed)
            return;

        float speed = -1f;

        if (PlayerMovement.Local != null)
            speed = PlayerMovement.Local.MoveSpeed;
        else if (NetWorld.Instance != null
                 && NetWorld.Instance.prefabs != null
                 && NetWorld.Instance.prefabs.Length > 0
                 && NetWorld.Instance.prefabs[0] != null)
        {
            PlayerMovement pm = NetWorld.Instance.prefabs[0]
                                    .GetComponentInChildren<PlayerMovement>(true);
            if (pm != null)
                speed = pm.MoveSpeed;
        }

        if (speed <= 0f)
            return;

        moveSpeed = speed * Mathf.Max(0.1f, speedRatio);
    }

    public void ApplySpeedMultiplier(float multiplier)
    {
        moveSpeed *= multiplier;
        if (Agent != null)
            Agent.speed *= multiplier;
    }

    public void ApplyStateSpeed(float ratio = 1f)
    {
        stateSpeedRatio = ratio;

        if (Agent != null && !IsDashing)
            Agent.speed = moveSpeed * ratio;
    }

    private void Update()
    {
        if (!IsDriver)
            return;

        if (IsCountdownActive())
        {
            if (Agent.enabled && Agent.isOnNavMesh)
            {
                if (Agent.hasPath)
                    Agent.ResetPath();
                Agent.velocity = Vector3.zero;
            }
            if (anim != null)
                anim.SetBool(AnimParams.IsMoving, false);
            return;
        }

        if (CheckGroundBelow())
            return;

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;
        if (dashTimer > 0f)
        {
            dashTimer -= Time.deltaTime;

            if (dashTimer <= 0f && Agent != null)
                Agent.speed = moveSpeed * stateSpeedRatio;
        }

        if (!Agent.enabled || !Agent.isOnNavMesh)
            return;

        currentState?.Update();

        if (GameState.CurrentGameMode != GameModeType.Push
            && currentState != FleeState
            && Time.time - lastUrgentThreatCheck >= 0.1f)
        {
            lastUrgentThreatCheck = Time.time;
            if (Detector.FindThreat() != null)
                ChangeState(FleeState);
        }

        Vector3 wishDir = Agent.desiredVelocity;
        wishDir.y = 0f;
        wishDir.Normalize();

        Agent.velocity = wishDir * Agent.speed;

        if (wishDir.sqrMagnitude > 0.001f)
        {
            transform.rotation = SmoothDamping.RotateTowards(
                transform.rotation, wishDir, rotateSpeed, Time.deltaTime);
        }

        bool isMoving = Agent.velocity.magnitude > 0.1f;
        if (anim != null)
            anim.SetBool(AnimParams.IsMoving, isMoving);
    }

    private bool IsCountdownActive()
    {
        var flow = LanGameFlow.Instance;
        return flow != null && flow.Phase != GamePhase.Playing;
    }

    private bool TryFindFallbackSpawnPos(out Vector3 pos)
    {
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || !e.IsBot || e.Transform == transform || e.IsOutOfPlay)
                continue;

            AIPlayerMovement other = e.Identity != null ? e.Identity.Bot : null;
            if (other != null && other.Agent != null && other.Agent.enabled && other.Agent.isOnNavMesh)
                return ScatterNear(e.Transform.position, out pos);
        }

        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.IsBot || e.Transform == null || e.IsOutOfPlay)
                continue;
            return ScatterNear(e.Transform.position, out pos);
        }

        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices != null && tri.vertices.Length > 0)
        {
            pos = tri.vertices[Random.Range(0, tri.vertices.Length)];
            return true;
        }

        pos = Vector3.zero;
        return false;
    }

    private bool ScatterNear(Vector3 center, out Vector3 pos)
    {
        Vector2 circle = Random.insideUnitCircle * FallbackScatterRadius;
        Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, FallbackScatterRadius, NavFilter))
        {
            pos = hit.position;
            return true;
        }

        pos = center;
        return true;
    }

    public bool TryGetWanderDestination(out Vector3 destination)
    {
        var collapse = TileCollapseManager.Instance;
        for (int i = 0; i < 15; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(5f, 20f);
            Vector3 candidate = transform.position
                + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 20f, NavFilter))
            {
                if (collapse != null && collapse.IsPositionDangerous(hit.position))
                    continue;
                destination = hit.position;
                return true;
            }
        }

        Vector3 fwd = transform.position + transform.forward * 5f;
        if (NavMesh.SamplePosition(fwd, out NavMeshHit fwHit, 10f, NavFilter))
        {
            destination = fwHit.position;
            return true;
        }

        destination = transform.position;
        return false;
    }

    private int GroundCheckPhase => Mathf.Abs(GetInstanceID()) % GroundCheckInterval;

    private Collider BodyCollider
    {
        get
        {
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider>();
            return bodyCollider;
        }
    }

    private bool CheckGroundBelow(bool immediate = false)
    {
        if (!immediate && (Time.frameCount + GroundCheckPhase) % GroundCheckInterval != 0)
            return false;
        if (IsEliminated || IsBeingAbsorbed)
            return false;

        Collider body = BodyCollider;
        if (body == null)
            return false;

        float pivotToFeet = Mathf.Max(0f, transform.position.y - body.bounds.min.y);

        Vector3 origin = transform.position + Vector3.up * GroundRayLift;
        float rayLength = GroundRayLift + pivotToFeet + GroundRayReach;

        float footRadius = Mathf.Max(body.bounds.extents.x, body.bounds.extents.z) * 0.9f;

        if (Physics.SphereCast(origin, footRadius, Vector3.down, out RaycastHit _, rayLength,
                               GameLayers.StandableMask, QueryTriggerInteraction.Ignore))
            return false;

        AwakeFallPhysics();
        return true;
    }

    private void RecoverTo(Vector3 pos)
    {
        Agent.Warp(pos);

        if (Agent.isOnNavMesh && Agent.hasPath)
            Agent.ResetPath();
    }

    private void AwakeFallPhysics()
    {
        StopBrain();
        PhysicsFall.Begin(gameObject);
    }

    private void ApplyAvoidancePriority(float scale)
    {
        if (Agent == null)
            return;

        int p = BasePriority - Mathf.RoundToInt((scale - 1f) * PriorityPerScale) + avoidanceJitter;
        Agent.avoidancePriority = Mathf.Clamp(p, 0, 99);
    }

    public void UpdateScaleOnAgent()
    {
        StartCoroutine(OnScaleChanged());
    }

    private IEnumerator OnScaleChanged()
    {
        if (!Agent.enabled)
        {
            yield return null;
            Agent.enabled = true;
            yield return null;
        }

        float s = GetMyAuthorityScale();

        Agent.radius = BaseAgentRadius * s;
        Agent.height = BaseAgentHeight * s;
        ApplyAvoidancePriority(s);

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f * s, NavFilter))
            Agent.Warp(hit.position);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsDriver)
            return;

        if (GameState.CurrentGameMode != GameModeType.Absorb)
            return;

        if (netId != null)
        {
            LanAbsorbTouch(other);
            return;
        }
    }

    private void LanAbsorbTouch(Collider other)
    {
        var mode = AbsorbMode.Instance;
        if (mode == null || netId == null)
            return;
        if (IsOutOfPlay)
            return;

        NetIdentity victim = other.GetComponentInParent<NetIdentity>();
        if (victim == null || victim == netId)
            return;
        if (NetEntity.IsJelly(victim))
            return;

        if (!GameTags.IsCharacterMainCollider(other))
            return;

        float myScale = GetMyAuthorityScale();
        float otherScale = NetEntity.ScaleOf(victim);
        if (otherScale >= myScale)
            return;

        mode.HostBotAbsorb(victim.NetId, netId.NetId);
    }

    public void ReportEliminated()
    {
        if (IsEliminated)
            return;

        if (!IsDriver)
            return;

        NetEntity.HostEliminate(netId);
    }

    public void ApplyEliminated()
    {
        if (IsEliminated)
            return;
        IsEliminated = true;

        StopBrain();
        enabled = false;

        if (anim != null)
            anim.SetBool(AnimParams.IsMoving, false);

        if (nameTagBillboard != null)
            nameTagBillboard.gameObject.SetActive(false);
    }

    public void StopForAbsorb()
    {
        IsBeingAbsorbed = true;
        StopBrain();
    }

    private void StopBrain()
    {
        if (Agent != null)
            Agent.enabled = false;

        currentState?.Exit();
        currentState = null;
    }

    public bool TryDash()
    {
        if (dashCooldownTimer > 0f || dashTimer > 0f)
            return false;
        if (!Agent.enabled || !Agent.isOnNavMesh)
            return false;
        if (IsEliminated || IsBeingAbsorbed)
            return false;

        dashCooldownTimer = dashCooldown;
        dashTimer = dashDuration;
        Agent.speed = dashSpeed;

        if (anim != null)
            anim.SetTrigger(AnimParams.Dash);

        if (visual != null)
            visual.SendTrigger(LanPlayerVisual.ANIM_DASH);
        return true;
    }

    private void ApplyBatModeVisibility()
    {
        if (batPivot == null)
            return;
        bool pushMode = GameState.CurrentGameMode == GameModeType.Push;
        batPivot.gameObject.SetActive(pushMode && !hideBatWhenIdle);
    }

    public void TryAttack()
    {
        if (GameState.CurrentGameMode != GameModeType.Push)
            return;
        if (IsAttacking || attackCooldownTimer > 0f)
            return;

        var dm = DataManager.Instance;
        if (dm == null)
            return;

        attackCooldownTimer = dm.BatCooldown;
        attackCoroutine = StartCoroutine(AttackSwingRoutine());

        BatSwing.Play(transform, anim, visual, GetMyAuthorityScale());
    }

    private IEnumerator AttackSwingRoutine()
    {
        var dm = DataManager.Instance;
        if (dm == null)
        {
            attackCoroutine = null;
            yield break;
        }

        bool hitDetected = false;
        float elapsed = 0f;

        while (elapsed < dm.BatSwingDuration)
        {
            elapsed += Time.deltaTime;

            if (!hitDetected)
                hitDetected = DetectBatHit();

            yield return null;
        }

        attackCoroutine = null;
    }

    private bool DetectBatHit()
    {
        if (!IsDriver || netId == null)
            return false;

        PushMode push = PushMode.Instance;

        if (push == null)
            return false;

        float scale = GetMyAuthorityScale();
        NetIdentity victim = BatArcQuery.Find(transform, netId, scale);

        if (victim == null)
            return false;

        push.HostBotBatHit(victim.NetId, netId.NetId);

        return true;
    }

    public void ApplyKnockbackFromNet(float dirX, float dirZ, float force)
    {
        if (IsEliminated || IsBeingAbsorbed)
            return;

        if (Agent != null && Agent.isOnNavMesh)
            Agent.ResetPath();

        if (knockbackCoroutine != null)
            StopCoroutine(knockbackCoroutine);
        knockbackCoroutine = StartCoroutine(
            KnockbackRoutine(Knockback.StartVelocity(new Vector3(dirX, 0f, dirZ), force)));
    }

    private IEnumerator KnockbackRoutine(Vector3 startVelocity)
    {
        if (Agent != null)
            Agent.enabled = false;

        float elapsed = 0f;

        while (Knockback.IsActive(elapsed))
        {
            if (IsEliminated || IsBeingAbsorbed)
                break;
            transform.position += Knockback.VelocityAt(startVelocity, elapsed) * Time.deltaTime;
            elapsed += Time.deltaTime;
            yield return null;
        }

        knockbackCoroutine = null;

        if (IsEliminated || IsBeingAbsorbed)
            yield break;
        if (Agent == null)
            yield break;

        if (CheckGroundBelow(true))
            yield break;

        Agent.enabled = true;

        if (Agent.isOnNavMesh)
        {
            Agent.Warp(transform.position);
            yield break;
        }

        Agent.enabled = false;

        if (TryPlaceOnNavMesh())
        {
            Agent.enabled = true;
            if (Agent.isOnNavMesh)
                Agent.Warp(transform.position);
            yield break;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 3f,
            currentState?.GetType().Name ?? "-");
#endif
    }
}
