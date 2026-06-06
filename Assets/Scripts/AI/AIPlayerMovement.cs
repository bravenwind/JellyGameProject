// ============================================================
// AIPlayerMovement.cs
// ============================================================
// 역할: NavMeshAgent 기반 AI 이동 컨트롤러 (FSM 패턴)
//
// 구조:
//   - FSM: AIBaseState 추상 클래스 상속, 상태별 스크립트 분리 (AI/FSM/)
//   - 탐지: AIDetector 컴포넌트에 위임 (SRP 분리)
//   - 흡수: OnTriggerEnter / RPC_BotAbsorbed
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;
using Photon.Realtime;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(AIDetector))]
public class AIPlayerMovement : MonoBehaviourPunCallbacks, IPunObservable
{
    // ─────────────────────────────────────────────────────────
    // Inspector 설정
    // ─────────────────────────────────────────────────────────
    [Header("이동")]
    public float moveSpeed = 6f;
    public float rotateSpeed = 10f;

    [Header("AI")]
    public float detectRadius = 15f;
    public float stateEvalRate = 0.4f;

    [Header("NavMeshAgent 기본 크기 (스케일 1 기준)")]
    public float baseAgentRadius = 0.5f;
    public float baseAgentHeight = 2.0f;

    [Header("대쉬")]
    public float dashSpeed = 50f;
    public float dashDuration = 0.2f;
    public float dashCooldown = 3f;

    [Header("이름표")]
    public NameTagBillboard nameTagBillboard;

    // ─────────────────────────────────────────────────────────
    // 컴포넌트 (상태 클래스들이 접근)
    // ─────────────────────────────────────────────────────────
    public NavMeshAgent Agent { get; private set; }
    public PlayerScaleController ScaleCtrl { get; private set; }
    public NavMeshQueryFilter NavFilter { get; private set; }
    public NavMeshPath CachedPath { get; private set; }
    public AIDetector Detector { get; private set; }

    private Animator _anim;
    private bool _wasMoving = false;
    private float _networkScale = 1f;
    private const float ScaleLerpSpeed = 10f;

    // ─────────────────────────────────────────────────────────
    // FSM
    // ─────────────────────────────────────────────────────────
    [Header("FSM (읽기 전용)")]
    [SerializeField] private string _dbg_currentState = "-";

    private AIBaseState _currentState;
    private bool _isTransitioning = false;
    public  AIBaseState CurrentState => _currentState;

    // 상태 인스턴스 (Start에서 1회 생성, 재사용)
    public AIWanderState WanderState { get; private set; }
    public AIChaseState  ChaseState  { get; private set; }
    public AIFleeState   FleeState   { get; private set; }
    public AIPushSurviveState PushSurviveState { get; private set; }

    private float _lastUrgentThreatCheck;
    public bool IsBeingAbsorbed { get; set; } = false;
    public bool IsEliminated { get; private set; } = false;

    private float _dashCooldownTimer;
    private float _dashTimer;
    private float _preDashSpeed;
    private float _attackCooldownTimer;
    public bool IsDashing => _dashTimer > 0f;

    private AIPlayerSync _aiSync;

    /// <summary>이 봇이 현재까지 획득한 점수 (AIPlayerSync를 통해 관리)</summary>
    public int CurrentScore => _aiSync != null ? _aiSync.CurrentScore : 0;

    // ─────────────────────────────────────────────────────────
    // 외부 프로퍼티
    // ─────────────────────────────────────────────────────────

    public float CurrentScale => transform.localScale.x;

    public float GetMyAuthorityScale()
    {
        if (_aiSync != null) return _aiSync.GetSyncedScale();
        return transform.localScale.x;
    }

    private void OnBotScaleChanged(float newScale)
    {
        _aiSync?.SyncScale(newScale);
    }

    // ─────────────────────────────────────────────────────────
    // 레지스트리 등록
    // ─────────────────────────────────────────────────────────
    private void OnEnable()
    {
        base.OnEnable();
        EntityRegistry.Register(this);
    }

    private void OnDisable()
    {
        base.OnDisable();
        EntityRegistry.Unregister(this);
    }

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        ScaleCtrl = GetComponent<PlayerScaleController>();
        Detector = GetComponent<AIDetector>();
        _aiSync = GetComponent<AIPlayerSync>();
        CachedPath = new NavMeshPath();
        _anim = GetComponentInChildren<Animator>();

        Detector.detectRadius = detectRadius;
        Detector.baseAgentRadius = baseAgentRadius;

        // [수정] NavMeshAgent가 스스로 오브젝트를 이동/회전시키지 못하게 원천 차단
        Agent.speed = moveSpeed;
        Agent.acceleration = 1000f; // 가속도를 극대화하여 즉시 최고속도 도달 (플레이어와 일치)
        Agent.angularSpeed = 0f;
        Agent.stoppingDistance = 0f;
        Agent.autoBraking = false;
        Agent.radius = baseAgentRadius;
        Agent.height = baseAgentHeight;

        // 중요: 에이전트가 시뮬레이션은 하되 직접 transform을 바꾸지 않게 설정할 수도 있지만,
        // 가장 깔끔한 방법은 에이전트의 원하는 속도(desiredVelocity)를 복사해서 수동 이동시키는 것입니다.
        // 여기서는 가속도(acceleration)를 무한대에 가깝게 높이는 것만으로도 플레이어와 속도가 거의 같아집니다.

        if (!photonView.ObservedComponents.Contains(this))
            photonView.ObservedComponents.Add(this);
    }

    private void Start()
    {
        // 상태 인스턴스 생성 (PlayerController 패턴과 동일)
        WanderState = new AIWanderState(this);
        ChaseState  = new AIChaseState(this);
        FleeState   = new AIFleeState(this);
        PushSurviveState = new AIPushSurviveState(this);

        if (!PhotonNetwork.IsMasterClient)
        {
            // 비마스터에서 봇의 로컬 흡수 처리 비활성화
            // (PlayerAbsorber가 켜져 있으면 GrowByJelly()가 로컬에서 발동해 스케일 충돌 발생)
            PlayerAbsorber absorber = GetComponent<PlayerAbsorber>();
            if (absorber != null) absorber.enabled = false;
            PlayerAbsorbingManager absorbMgr = GetComponent<PlayerAbsorbingManager>();
            if (absorbMgr != null) absorbMgr.enabled = false;

            // Cloth 제거 (스케일 동기화와 충돌하여 모델 찌그러짐 방지)
            SoftBody3D softBody = GetComponentInChildren<SoftBody3D>();
            if (softBody != null) softBody.RemoveCloth();

            Agent.enabled = false;
            return;
        }

        if (ScaleCtrl != null)
            ScaleCtrl.OnScaleValueChanged += OnBotScaleChanged;

        StartCoroutine(InitAndRun());
    }

    /// <summary>NavMesh 위에 스폰 확정 후 FSM 시작</summary>
    private IEnumerator InitAndRun()
    {
        Agent.enabled = false;

        NavFilter = new NavMeshQueryFilter
        {
            agentTypeID = Agent.agentTypeID,
            areaMask    = NavMesh.AllAreas
        };

        // NavMesh 위 위치 확정 (NavFilter 우선, agent type 미스매치 등으로 실패 시 AllAreas 폴백)
        bool foundOnNavMesh = false;
        float elapsed = 0f;
        while (elapsed < 5f)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 100f, NavFilter)
                || NavMesh.SamplePosition(transform.position, out hit, 100f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                foundOnNavMesh = true;
                break;
            }
            elapsed += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        // 2차 폴백: 잘못된 위치(예: 원점)에 스폰된 경우 살아있는 다른 엔티티 위치로 텔레포트 후 재시도
        if (!foundOnNavMesh && TryFindFallbackSpawnPos(out Vector3 fallback))
        {
            Debug.LogWarning($"[AIBot] {name} 초기 위치 {transform.position}에서 NavMesh 미발견 → 폴백 {fallback}로 이동");
            transform.position = fallback;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit fhit, 50f, NavMesh.AllAreas))
            {
                transform.position = fhit.position;
                foundOnNavMesh = true;
            }
        }

        // NavMesh 위치 못 찾으면 Agent 활성화 시도 자체를 스킵 (Unity 에러 방지)
        if (!foundOnNavMesh)
        {
            Debug.LogWarning($"[AIBot] {name} NavMesh 위치 탐색 실패 - Agent 비활성 유지");
            yield break;
        }

        Agent.enabled = true;

        // isOnNavMesh 대기
        float timeout = 3f;
        while (!Agent.isOnNavMesh && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (!Agent.isOnNavMesh)
        {
            Debug.LogWarning($"[AIBot] {name} NavMesh 배치 실패");
            yield break;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit warpHit, 5f, NavFilter)
            || NavMesh.SamplePosition(transform.position, out warpHit, 5f, NavMesh.AllAreas))
            Agent.Warp(warpHit.position);

        Agent.avoidancePriority = Random.Range(20, 80);

        // 첫 상태 진입
        if (GameState.CurrentGameMode == GameModeType.Push)
            ChangeState(PushSurviveState);
        else
            ChangeState(WanderState);
        StartCoroutine(StateEvalLoop());
    }

    // ─────────────────────────────────────────────────────────
    // 상태 관리 (PlayerController.ChangeState 패턴)
    // ─────────────────────────────────────────────────────────

    public void ChangeState(AIBaseState newState)
    {
        if (_currentState == newState) return;
        if (_isTransitioning) return;

        _isTransitioning = true;
        _currentState?.Exit();
        _currentState = newState;
        _currentState?.Enter();
        _dbg_currentState = _currentState?.GetType().Name ?? "-";
        _isTransitioning = false;
    }

    /// <summary>주기적으로 상태 전환 평가 (우선순위: Flee > Chase > Wander)</summary>
    private IEnumerator StateEvalLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(stateEvalRate);

            if (!Agent.enabled || !Agent.isOnNavMesh) continue;

            if (GameState.CurrentGameMode == GameModeType.Push)
            {
                if (FindThreat() != null) { ChangeState(FleeState); continue; }
                ChangeState(PushSurviveState);
                continue;
            }

            // 위험 타일 위면 다른 판단보다 우선적으로 안전한 곳으로 도망
            var collapse = TileCollapseManager.Instance;
            if (collapse != null && collapse.IsPositionDangerous(transform.position))
            {
                if (TryGetWanderDestination(out Vector3 safe))
                    Agent.SetDestination(safe);
                ChangeState(WanderState);
                continue;
            }

            EvaluateAndTransition();
        }
    }

    /// <summary>현재 상황을 평가하여 적절한 상태로 전환</summary>
    public void EvaluateAndTransition()
    {
        if (FindThreat() != null) { ChangeState(FleeState); return; }

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            ChangeState(PushSurviveState);
            return;
        }

        if (FindTargetToChase() != null) { ChangeState(ChaseState); return; }
        ChangeState(WanderState);
    }

    // ─────────────────────────────────────────────────────────
    // Update: 현재 상태 업데이트 + 회전 + 애니메이션
    // ─────────────────────────────────────────────────────────
    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _networkScale, Time.deltaTime * ScaleLerpSpeed);
            return;
        }

        if (!Agent.enabled || !Agent.isOnNavMesh) return;

        if (_dashCooldownTimer > 0f) _dashCooldownTimer -= Time.deltaTime;
        if (_attackCooldownTimer > 0f) _attackCooldownTimer -= Time.deltaTime;
        if (_dashTimer > 0f)
        {
            _dashTimer -= Time.deltaTime;
            if (_dashTimer <= 0f)
                Agent.speed = _preDashSpeed;
        }

        if (GameState.CurrentGameMode == GameModeType.Push)
            CheckGroundBelow();

        // 현재 상태 Update (목적지 설정 등)
        _currentState?.Update();

        // 긴급 위협 감지: FleeState가 아닐 때 0.1초 간격으로 위협 체크
        if (_currentState != FleeState
            && Time.time - _lastUrgentThreatCheck >= 0.1f)
        {
            _lastUrgentThreatCheck = Time.time;
            if (FindThreat() != null)
                ChangeState(FleeState);
        }

        // ─────────────────────────────────────────────────────────
        // [개선] 플레이어의 MoveAndRotate()와 연산 공식 일치시키기
        // ─────────────────────────────────────────────────────────

        // Agent.desiredVelocity는 다음 목적지를 향한 가속도가 배제된 '순수 희망 방향 벡터'입니다.
        Vector3 wishDir = Agent.desiredVelocity;
        wishDir.y = 0f;
        wishDir.Normalize();

        // 플레이어의 finalMove = inputDir * moveSpeed; 공식과 완벽 동기화
        // NavMeshAgent가 직접 움직이는 속도를 제어하기 위해 agent.velocity를 강제 세팅하거나 수동 이동
        Agent.velocity = wishDir * Agent.speed;

        // 회전 공식도 플레이어와 완벽히 일치 (wishDir 기반으로 변경)
        if (wishDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(wishDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                rotateSpeed * Time.deltaTime);
        }

        // 애니메이터 처리
        bool isMoving = Agent.velocity.magnitude > 0.1f;
        if (_anim != null)
            _anim.SetBool("IsMoving", isMoving);

        if (isMoving != _wasMoving && PhotonNetwork.InRoom)
        {
            _wasMoving = isMoving;
            photonView.RPC(nameof(RPC_SetIsMoving), RpcTarget.Others, isMoving);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 탐지 함수 (AIDetector에 위임, 상태 클래스 호환용 래퍼)
    // ─────────────────────────────────────────────────────────

    public float GetMyScale()
        => transform.localScale.x;

    public Transform FindThreat() => Detector.FindThreat();
    public Transform FindPrey() => Detector.FindPrey();
    public Transform FindTargetToChase() => Detector.FindTargetToChase();
    public Transform FindNearestJelly() => Detector.FindNearestJelly();

    /// <summary>
    /// 스폰 위치가 NavMesh로부터 멀리 떨어진 경우 폴백 위치 탐색.
    /// 우선순위: 살아있는 다른 봇(NavMesh 위) → 살아있는 플레이어 → NavMesh 삼각망 정점
    /// </summary>
    private bool TryFindFallbackSpawnPos(out Vector3 pos)
    {
        // 1. 살아있는 다른 봇 (NavMesh 위에 있다고 검증된 경우)
        foreach (var bot in FindObjectsByType<AIPlayerMovement>(FindObjectsSortMode.None))
        {
            if (bot == null || bot == this || bot.IsEliminated) continue;
            if (bot.Agent != null && bot.Agent.enabled && bot.Agent.isOnNavMesh)
            {
                pos = bot.transform.position;
                return true;
            }
        }
        // 2. 살아있는 플레이어
        foreach (var p in FindObjectsByType<NetworkPlayerSync>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            pos = p.transform.position;
            return true;
        }
        // 3. NavMesh 삼각망 정점 (마지막 수단)
        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices != null && tri.vertices.Length > 0)
        {
            pos = tri.vertices[Random.Range(0, tri.vertices.Length)];
            return true;
        }
        pos = Vector3.zero;
        return false;
    }

    /// <summary>배회용 랜덤 NavMesh 위치 탐색 (붕괴 예정 타일은 회피)</summary>
    public bool TryGetWanderDestination(out Vector3 destination)
    {
        var collapse = TileCollapseManager.Instance;
        for (int i = 0; i < 15; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(5f, 20f);
            Vector3 candidate = transform.position
                + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 20f, NavFilter)
                || NavMesh.SamplePosition(candidate, out hit, 20f, NavMesh.AllAreas))
            {
                if (collapse != null && collapse.IsPositionDangerous(hit.position)) continue;
                destination = hit.position;
                return true;
            }
        }

        // 최후 수단: 앞 방향 소폭 이동
        Vector3 fwd = transform.position + transform.forward * 5f;
        if (NavMesh.SamplePosition(fwd, out NavMeshHit fwHit, 10f, NavFilter)
            || NavMesh.SamplePosition(fwd, out fwHit, 10f, NavMesh.AllAreas))
        {
            destination = fwHit.position;
            return true;
        }

        destination = transform.position;
        return false;
    }

    // ─────────────────────────────────────────────────────────
    // Push 모드: 발 밑 지면 확인 → 없으면 낙하
    // ─────────────────────────────────────────────────────────

    private void CheckGroundBelow()
    {
        if (Time.frameCount % 15 != 0) return;
        if (IsEliminated || IsBeingAbsorbed) return;

        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, 3f)) return;

        Debug.Log($"[AIBot] {name} 발 밑 지면 없음 → 낙하 전환");

        if (Agent != null) Agent.enabled = false;
        _currentState?.Exit();
        _currentState = null;

        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    // ─────────────────────────────────────────────────────────
    // 외부 콜백 (PlayerScaleController 호환)
    // ─────────────────────────────────────────────────────────

    /// <summary>봇 스케일 증가 완료 시 PlayerScaleController가 호출</summary>
    public void RecenterCC()
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

        // 에이전트 캡슐 크기 갱신
        float s = transform.localScale.x;
        Agent.radius = baseAgentRadius * s;
        Agent.height = baseAgentHeight * s;
        Agent.avoidancePriority = Mathf.Max(0, Agent.avoidancePriority - 5);

        // NavMesh 위로 Warp (NavFilter 실패 시 AllAreas 폴백)
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f * s, NavFilter)
            || NavMesh.SamplePosition(transform.position, out hit, 5f * s, NavMesh.AllAreas))
            Agent.Warp(hit.position);
    }

    // ─────────────────────────────────────────────────────────
    // 흡수 처리 (플레이어 ↔ AI)
    // ─────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // ── 더 작은 플레이어 흡수 ──
        NetworkPlayerSync player = other.GetComponentInParent<NetworkPlayerSync>();
        if (player != null)
        {
            float myScale = GetMyAuthorityScale();
            float playerScale = NetworkPlayerSync.GetPlayerSyncedScale(player.photonView.Owner);
            if (playerScale >= myScale) return;

            player.photonView.RPC("RPC_GetAbsorbed", RpcTarget.All, photonView.ViewID);
            ScaleCtrl?.GrowByAbsorbing(playerScale);
            return;
        }

        // ── 더 작은 AI 봇 흡수 ──
        AIPlayerMovement otherBot = other.GetComponentInParent<AIPlayerMovement>();
        if (otherBot != null && otherBot != this && !otherBot.IsBeingAbsorbed)
        {
            float myScale = GetMyAuthorityScale();
            float otherScale = otherBot.GetMyAuthorityScale();
            if (otherScale >= myScale) return;

            otherBot.photonView.RPC(nameof(RPC_BotAbsorbed), RpcTarget.All, photonView.ViewID);
            ScaleCtrl?.GrowByAbsorbing(otherScale);
            return;
        }
    }

    /// <summary>
    /// 초콜릿 등으로 탈락 처리. 리더보드/이름표 제거, AI/Agent 정지.
    /// 오브젝트는 파괴하지 않고 둥둥 떠다니게 유지.
    /// 마스터에서 호출되면 RPC로 모든 클라이언트에 전파한다.
    /// </summary>
    public void OnEliminated()
    {
        if (IsEliminated) return;

        if (PhotonNetwork.IsMasterClient && photonView != null)
            photonView.RPC(nameof(RPC_OnEliminated), RpcTarget.All);
        else
            ApplyEliminatedLocally();
    }

    [PunRPC]
    private void RPC_OnEliminated()
    {
        ApplyEliminatedLocally();
    }

    private void ApplyEliminatedLocally()
    {
        if (IsEliminated) return;
        IsEliminated = true;

        if (Agent != null) Agent.enabled = false;
        _currentState?.Exit();
        _currentState = null;
        enabled = false;

        if (nameTagBillboard != null) nameTagBillboard.gameObject.SetActive(false);

        if (_aiSync != null && PhotonNetwork.IsMasterClient)
            _aiSync.ClearBotProperties();
    }

    /// <summary>[RPC] 이 봇이 흡수당했을 때 (모든 클라이언트에서 실행)</summary>
    [PunRPC]
    private void RPC_BotAbsorbed(int absorberViewID)
    {
        if (IsBeingAbsorbed) return;
        IsBeingAbsorbed = true;
        Debug.Log(this.name + "/RPC_BotAbsorbed : AI 플레이어 흡수됨.");
        StartCoroutine(BotAbsorbedSequence(absorberViewID));
    }

    private IEnumerator BotAbsorbedSequence(int absorberViewID)
    {
        if (Agent != null) Agent.enabled = false;
        _currentState?.Exit();
        _currentState = null;

        PhotonView absorberView = PhotonView.Find(absorberViewID);
        Transform absorberTf = absorberView?.transform;

        Vector3 startScale = transform.localScale;
        float elapsed = 0f;
        const float duration = 0.8f;
        const float moveSpeed = 12f;
        const float snapDist = 0.4f;

        while (elapsed < duration)
        {
            if (absorberTf != null)
            {
                if (Vector3.Distance(transform.position, absorberTf.position) <= snapDist) break;
                transform.position = Vector3.MoveTowards(transform.position, absorberTf.position, moveSpeed * Time.deltaTime);
            }
            transform.localScale = Vector3.Lerp(startScale, Vector3.one * 0.05f, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // 게임이 이미 끝난 상태(Result)면 Destroy를 보내지 않는다.
        // LoadLevel과 Destroy 이벤트가 서로 다른 채널로 전파되어,
        // 비마스터에서 씬 전환 후 stale Destroy가 도착하면
        // "Could not find PhotonView" 에러가 발생하기 때문이다.
        // 씬 전환 시 PUN이 네트워크 오브젝트를 자동 정리하므로 안전하다.
        if (PhotonNetwork.IsMasterClient && GameState.Phase == GamePhase.Playing)
            PhotonNetwork.Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────
    // 마스터 클라이언트 교체 시 AI 이어받기
    // ─────────────────────────────────────────────────────────

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (IsBeingAbsorbed) return;

        Debug.Log($"[AIBot] {name} — 새 MasterClient가 AI 제어 이어받기");

        PlayerAbsorber absorber = GetComponent<PlayerAbsorber>();
        if (absorber != null) absorber.enabled = true;
        PlayerAbsorbingManager absorbMgr = GetComponent<PlayerAbsorbingManager>();
        if (absorbMgr != null) absorbMgr.enabled = true;

        if (ScaleCtrl != null)
            ScaleCtrl.OnScaleValueChanged += OnBotScaleChanged;

        StartCoroutine(InitAndRun());
    }

    // ─────────────────────────────────────────────────────────
    // 스케일 동기화 (IPunObservable)
    // ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.localScale.x);
        }
        else
        {
            _networkScale = (float)stream.ReceiveNext();
        }
    }

    // ─────────────────────────────────────────────────────────
    // 대쉬 (Push 모드)
    // ─────────────────────────────────────────────────────────

    public bool TryDash()
    {
        if (_dashCooldownTimer > 0f || _dashTimer > 0f) return false;
        if (!Agent.enabled || !Agent.isOnNavMesh) return false;
        if (IsEliminated || IsBeingAbsorbed) return false;

        _dashCooldownTimer = dashCooldown;
        _dashTimer = dashDuration;
        _preDashSpeed = Agent.speed;
        Agent.speed = dashSpeed;

        if (_anim != null) _anim.SetTrigger("Dash");
        if (PhotonNetwork.InRoom)
            photonView.RPC(nameof(RPC_PlayDash), RpcTarget.Others);
        return true;
    }

    [PunRPC]
    private void RPC_PlayDash()
    {
        if (_anim != null) _anim.SetTrigger("Dash");
    }

    // ─────────────────────────────────────────────────────────
    // 공격 (Push 모드)
    // ─────────────────────────────────────────────────────────

    public bool TryAttack()
    {
        if (_attackCooldownTimer > 0f) return false;
        if (!PhotonNetwork.IsMasterClient) return false;
        if (IsEliminated || IsBeingAbsorbed) return false;
        if (GameState.CurrentGameMode != GameModeType.Push) return false;

        var dm = DataManager.Instance;
        if (dm == null) return false;

        float scale = transform.localScale.x;
        float range = dm.batRange * scale;
        float arcAngle = dm.batArcAngle;
        float pushForce = dm.batPushForce * (scale / dm.startingScale);

        Vector3 center = transform.position + Vector3.up * scale;
        int mask = LayerMask.GetMask("Player") | LayerMask.GetMask("Edible");
        Collider[] hits = Physics.OverlapSphere(center, range, mask);

        foreach (var hit in hits)
        {
            if (hit.transform.root == transform.root) continue;

            Vector3 dirToTarget = hit.transform.position - transform.position;
            dirToTarget.y = 0;
            if (dirToTarget.sqrMagnitude < 0.01f) continue;

            float angle = Vector3.Angle(transform.forward, dirToTarget);
            if (angle > arcAngle * 0.5f) continue;

            Vector3 pushDir = dirToTarget.normalized;

            NetworkPlayerSync playerSync = hit.GetComponentInParent<NetworkPlayerSync>();
            if (playerSync != null && playerSync.photonView.Owner != null)
            {
                playerSync.photonView.RPC(nameof(NetworkPlayerSync.RPC_ApplyKnockback),
                    playerSync.photonView.Owner, pushDir.x, pushDir.z, pushForce);
                ApplyAttackReward(dm, scale);
                return true;
            }

            AIPlayerMovement otherBot = hit.GetComponentInParent<AIPlayerMovement>();
            if (otherBot != null && otherBot != this && !otherBot.IsEliminated && !otherBot.IsBeingAbsorbed)
            {
                otherBot.photonView.RPC(nameof(RPC_ApplyKnockback), RpcTarget.All,
                    pushDir.x, pushDir.z, pushForce);
                ApplyAttackReward(dm, scale);
                return true;
            }
        }

        return false;
    }

    private void ApplyAttackReward(DataManager dm, float scale)
    {
        _attackCooldownTimer = dm.batCooldown;
        float growth = dm.batHitGrowth / Mathf.Max(scale, 1f);
        if (ScaleCtrl != null) ScaleCtrl.GrowByBatHit(growth);
        if (_anim != null) _anim.SetTrigger("Attack");
        if (PhotonNetwork.InRoom)
            photonView.RPC(nameof(RPC_PlayAttack), RpcTarget.Others);
    }

    [PunRPC]
    private void RPC_PlayAttack()
    {
        if (_anim != null) _anim.SetTrigger("Attack");
    }

    // ─────────────────────────────────────────────────────────
    // 애니메이션 동기화 RPC (비마스터 클라이언트용)
    // ─────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_SetIsMoving(bool isMoving)
    {
        if (_anim != null)
            _anim.SetBool("IsMoving", isMoving);
    }

    // ─────────────────────────────────────────────────────────
    // 대쉬 밀치기 (넉백)
    // ─────────────────────────────────────────────────────────

    private Coroutine _knockbackCoroutine;

    [PunRPC]
    public void RPC_ApplyKnockback(float dirX, float dirZ, float force)
    {
        if (IsEliminated || IsBeingAbsorbed) return;

        if (Agent != null && Agent.isOnNavMesh)
            Agent.ResetPath();

        if (_knockbackCoroutine != null)
            StopCoroutine(_knockbackCoroutine);
        _knockbackCoroutine = StartCoroutine(KnockbackRoutine(new Vector3(dirX, 0f, dirZ).normalized, force));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Vector3 dir, float force)
    {
        if (Agent != null) Agent.enabled = false;

        float elapsed = 0f;
        const float duration = 0.4f;
        CharacterController cc = GetComponent<CharacterController>();

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            Vector3 move = Vector3.Lerp(dir * force, Vector3.zero, t);
            if (cc != null && cc.enabled)
                cc.Move(move * Time.deltaTime);
            else
                transform.position += move * Time.deltaTime;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (Agent != null && !IsEliminated && !IsBeingAbsorbed)
        {
            Agent.enabled = true;
            if (Agent.isOnNavMesh)
                Agent.Warp(transform.position);
        }

        _knockbackCoroutine = null;
    }

    // ─────────────────────────────────────────────────────────
    // 디버그
    // ─────────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 3f,
            _dbg_currentState);
#endif
    }
}
