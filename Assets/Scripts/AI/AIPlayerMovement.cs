// ============================================================
// AIPlayerMovement.cs
// ============================================================
// 역할: NavMeshAgent 기반 AI 이동 컨트롤러 (FSM 패턴)
//
// 구조:
//   - FSM: AIBaseState 추상 클래스 상속, 상태별 스크립트 분리 (AI/FSM/)
//   - 탐지: AIDetector 컴포넌트에 위임 (SRP 분리)
//   - 흡수: OnTriggerEnter → LanAbsorbTouch
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using JellyNet;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AIDetector))]
public class AIPlayerMovement : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // Inspector 설정
    // ─────────────────────────────────────────────────────────
    [Header("이동")]
    public float moveSpeed = 6f;
    public float rotateSpeed = 10f;

    // ═════════════════════════════════════════════════════════
    //  플레이어와 이동 속도 맞추기
    // ═════════════════════════════════════════════════════════
    //
    // ★ 왜 코드에서 맞추는가
    //   두 프리팹의 값이 조용히 벌어져 있었다.
    //
    //     NetworkPlayer_Bear.moveSpeed = 18
    //     AIPlayer_Bear.moveSpeed      =  6      ← 3배 느림
    //
    //   코드 기본값은 양쪽 다 6인데, 플레이어만 인스펙터에서 올리고 봇은 잊은 것이다.
    //   Update의 "플레이어의 이동 공식과 완벽 동기화" 주석이 말해주듯 원래 의도는
    //   같은 속도였다 — 공식은 맞췄는데 값이 안 맞았다.
    //
    //   결과:
    //     밀치기 → 봇이 도망을 못 쳐 너무 쉽게 떨어진다
    //     흡수   → 봇이 플레이어를 영영 못 잡고, 플레이어는 봇을 마음대로 잡는다
    //
    //   인스펙터 숫자 두 개를 손으로 맞추는 것으로 끝내면 다음에 또 벌어진다.
    //   플레이어 프리팹의 값을 읽어 쓰면 한쪽만 바꿔도 자동으로 따라온다.

    [Header("플레이어와 속도 맞추기")]
    [Tooltip("켜면 플레이어 프리팹의 moveSpeed를 그대로 쓴다. 위 moveSpeed 값은 무시된다.")]
    public bool matchPlayerSpeed = true;

    [Tooltip("플레이어 대비 배율. 1이면 완전히 동일. 봇을 조금 느리게 하려면 0.9 등.")]
    public float speedRatio = 1f;

    [Header("AI")]
    public float detectRadius = 15f;

    // ★ 상태 재평가 주기.
    //   0.4초는 흡수 모드(배회↔추격↔도주)에는 넉넉하지만 밀치기에는 느리다.
    //   밀치기는 PushSurviveState 하나만 쓰므로 재평가가 자주 돌아도 비용이 거의 없다.
    public float stateEvalRate = 0.15f;

    [Header("NavMeshAgent 기본 크기 (스케일 1 기준)")]
    public float baseAgentRadius = 0.5f;
    public float baseAgentHeight = 2.0f;

    [Header("Push 모드 (빠따/대쉬)")]
    public Transform batPivot;
    public bool hideBatWhenIdle = true;
    public float dashSpeed = 80f;
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
    private LanPlayerVisual _visual;
    private bool _wasMoving = false;

    // ─────────────────────────────────────────────────────────
    // FSM
    // ─────────────────────────────────────────────────────────
    [Header("FSM (읽기 전용)")]
    [SerializeField] private string _dbg_currentState = "-";

    private AIBaseState _currentState;
    private bool _isTransitioning = false;

    // 상태 인스턴스 (Start에서 1회 생성, 재사용)
    public AIWanderState WanderState { get; private set; }
    public AIChaseState  ChaseState  { get; private set; }
    public AIFleeState   FleeState   { get; private set; }
    public AIPushSurviveState PushSurviveState { get; private set; }

    private float _lastUrgentThreatCheck;
    public bool IsBeingAbsorbed { get; set; } = false;
    public bool IsEliminated { get; private set; } = false;

    /// <summary>봇이 게임에서 빠졌는지(탈락 또는 흡수 진행 중). "이 엔티티가 게임에서 빠졌나?"
    /// 판정의 단일 출처 — 인디케이터/충돌/리더보드가 모두 이 값을 본다. (G6)</summary>
    public bool IsOutOfPlay => IsEliminated || IsBeingAbsorbed;

    private float _dashCooldownTimer;
    private float _dashTimer;
    private float _preDashSpeed;
    private float _attackCooldownTimer;
    private Coroutine _attackCoroutine;
    public bool IsDashing => _dashTimer > 0f;
    public bool IsAttacking => _attackCoroutine != null;


    // ═════════════════════════════════════════════════════════
    //  [LAN 이식] 봇 권위 판정
    // ═════════════════════════════════════════════════════════
    //
    // ★ 봇은 호스트에서만 생각하고, 나머지는 결과만 본다
    //   봇은 전부 호스트 소유 NetIdentity라 그 판정을 IsMine 하나로 표현할 수 있다.
    //
    //   접속이 없으면(오프라인 테스트) 혼자 다 굴린다 — 안 그러면 봇이 얼어붙는다.
    private NetIdentity _netId;
    private LanBotState _botSync;

    /// <summary>이 기계가 이 봇의 두뇌를 돌리는가.</summary>
    private bool IsDriver
    {
        get
        {
            //봇은 전부 NetWorld가 스폰하므로 _netId가 없는 봇은 없다
            return _netId != null && _netId.IsMineOrOffline;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 외부 프로퍼티
    // ─────────────────────────────────────────────────────────

    /// <summary>이 봇의 netId. LanPlayerState.EntityId와 같은 역할이다.
    /// NetIdentity를 Awake에서 캐시해두므로 매번 계층을 훑지 않는다.</summary>
    public int EntityId => _netId != null ? _netId.NetId : 0;

    /// <summary>Awake에서 캐시해둔 NetIdentity. LanPlayerState.Identity와 같은 역할이다.</summary>
    public NetIdentity Identity => _netId;

    /// <summary>이 봇의 네트워크 상태. Awake에서 캐시해둔 것을 그대로 준다.</summary>
    public LanBotState BotState => _botSync;

    /// <summary>
    /// 이 봇의 크기. 판정에 쓰는 값의 출처는 PlayerScaleController 하나다.
    ///
    /// ★ 예전엔 transform.localScale.x를 그대로 돌려줬다
    ///   그건 '지금 화면에 보이는 크기'라, 커지는 연출이 도는 동안(약 0.3초)
    ///   사람 쪽 판정값(currentScaleValue = 연출이 끝난 목표 크기)과 어긋났다.
    ///   그 사이 봇은 실제보다 작게 취급돼 흡수당하기 쉬웠다.
    /// </summary>
    public float GetMyAuthorityScale()
    {
        return ScaleCtrl != null ? ScaleCtrl.currentScaleValue : transform.localScale.x;
    }

    // ─────────────────────────────────────────────────────────
    // 레지스트리 등록
    // ─────────────────────────────────────────────────────────
    private void OnEnable()
    {
        EntityRegistry.Register(this);
    }

    private void OnDisable()
    {
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
        _netId = GetComponent<NetIdentity>();
        _botSync = GetComponent<LanBotState>();
        CachedPath = new NavMeshPath();
        _anim = GetComponentInChildren<Animator>();
        _visual = GetComponentInParent<LanPlayerVisual>();

        Detector.detectRadius = detectRadius;
        Detector.baseAgentRadius = baseAgentRadius;

        ApplyPlayerSpeed();

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
    }

    private void Start()
    {
        // 상태 인스턴스 생성 (PlayerController 패턴과 동일)
        WanderState = new AIWanderState(this);
        ChaseState  = new AIChaseState(this);
        FleeState   = new AIFleeState(this);
        PushSurviveState = new AIPushSurviveState(this);

        // 배트는 Push 모드에서만 활성화 (호스트·클라 공통)
        ApplyBatModeVisibility();

        if (!IsDriver)
        {
            // 봇을 굴리지 않는 쪽에서는 로컬 흡수 처리를 끈다
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

        _initCoroutine = StartCoroutine(InitAndRun());
    }

    // [BOT-1] InitAndRun 코루틴 핸들. 탈락/흡수가 확정되면 이 코루틴을 명시적으로 멈춰야 한다 —
    // enabled=false는 이미 실행 중인 코루틴을 멈추지 못해(O5와 동일), NavMesh 탐색(최대 5초)
    // 도중에 탈락 RPC가 와도 코루틴이 계속 진행해 Agent를 다시 켜고 FSM을 재기동 → 죽은 봇이
    // 되살아나 배회하는 문제가 있었다.
    private Coroutine _initCoroutine;

    /// <summary>NavMesh 위에 스폰 확정 후 FSM 시작</summary>
    private IEnumerator InitAndRun()
    {
        // 이미 판 밖이면 시작조차 하지 않는다(탈락·흡수된 봇이 되살아나는 걸 막는다).
        if (IsEliminated || IsBeingAbsorbed) { _initCoroutine = null; yield break; }

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
            _initCoroutine = null;
            yield break;
        }

        // [BOT-1] 탐색 대기(최대 5초) 동안 탈락/흡수가 확정됐으면 여기서 중단 — Agent를 다시 켜지 않는다.
        if (IsEliminated || IsBeingAbsorbed) { _initCoroutine = null; yield break; }

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

        // [BOT-1] isOnNavMesh 대기 동안 탈락/흡수됐으면 FSM을 기동하지 않는다.
        if (IsEliminated || IsBeingAbsorbed) { _initCoroutine = null; yield break; }

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
        _initCoroutine = null;
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

            // 정상적으로 추락 중인 봇은 Agent가 꺼져 있다(CheckGroundBelow/AwakePhysicsOnTile).
            // 그런 봇은 물리로 떨어지게 그대로 둔다.
            if (!Agent.enabled) continue;

            // Agent는 켜져 있는데 NavMesh 밖에 있는 봇 = 넉백/붕괴로 발판 없는 허공에 박제된 경우.
            // Push 모드는 CheckGroundBelow가 별도로 낙하시키므로, 낙하가 아닌 '안전 타일 복귀'를
            // 설계로 쓰는 흡수 모드에서만 가장 가까운 안전 타일로 Warp해 floating을 해소한다.
            // (이 분기가 없으면 흡수 모드에서 허공에 뜬 봇이 낙하도 복구도 안 돼 영구히 떠 있다.)
            if (!Agent.isOnNavMesh)
            {
                if (GameState.CurrentGameMode != GameModeType.Push)
                {
                    var c = TileCollapseManager.Instance;
                    if (c != null && c.FindNearestSafeTile(transform.position, out Vector3 offSafe)
                        && (NavMesh.SamplePosition(offSafe, out NavMeshHit offHit, 10f, NavFilter)
                            || NavMesh.SamplePosition(offSafe, out offHit, 10f, NavMesh.AllAreas)))
                    {
                        Agent.Warp(offHit.position);
                        if (Agent.isOnNavMesh && Agent.hasPath) Agent.ResetPath();
                    }
                }
                continue;
            }

            // 발판이 없는 허공(붕괴된 타일 자리의 잔존 NavMesh 등) 위에 떠 있으면
            // 가장 가까운 안전 타일로 즉시 복귀시킨다. 이렇게 해야 AI가 공중에서
            // 도달 불가능한 목적지를 못 찾아 WanderState로 Idle 박제되는 현상이 사라진다.
            var collapse = TileCollapseManager.Instance;
            if (collapse != null && collapse.IsOverVoid(transform.position))
            {
                if (collapse.FindNearestSafeTile(transform.position, out Vector3 safeTile)
                    && (NavMesh.SamplePosition(safeTile, out NavMeshHit voidHit, 10f, NavFilter)
                        || NavMesh.SamplePosition(safeTile, out voidHit, 10f, NavMesh.AllAreas)))
                {
                    Agent.Warp(voidHit.position);
                    // 복귀 직후엔 허공을 향하던 기존 경로가 무효이므로 비우고, 다음 평가 때
                    // 모드에 맞는 상태(Wander/PushSurvive 등)가 새 목적지를 잡게 한다.
                    if (Agent.isOnNavMesh && Agent.hasPath) Agent.ResetPath();
                }
                continue;
            }

            if (GameState.CurrentGameMode == GameModeType.Push)
            {
                if (FindThreat() != null) { ChangeState(FleeState); continue; }
                ChangeState(PushSurviveState);
                continue;
            }

            // 위험 타일 위면 다른 판단보다 우선적으로 안전한 곳으로 도망.
            // 속도는 플레이어와 동일하게 두고, 급한 회피는 대쉬(짧은 버스트)로 처리한다.
            if (collapse != null && collapse.IsPositionDangerous(transform.position))
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

    /// <summary>현재 상황을 평가하여 적절한 상태로 전환</summary>
    public void EvaluateAndTransition()
    {
        // ★ 밀치기 모드 판정을 위협 검사보다 <b>먼저</b> 한다.
        //
        //   예전엔 FindThreat()가 먼저였다. 그런데 밀치기에서는 배트를 맞히면 커지므로,
        //   상대가 한 대만 맞혀도 그 순간부터 '나보다 큰 상대'가 된다.
        //   그러면 봇이 PushSurviveState를 버리고 FleeState로 넘어가 <b>도망만 다닌다.</b>
        //   공격도, 발판 회피도, 대쉬도 전부 PushSurviveState에 들어 있는데 그게 안 돈다.
        //   "AI가 멍청해 보이는" 증상의 실체가 이것이다.
        //
        //   FleeState는 흡수 모드용이다 — 거기서는 큰 상대에게 먹히므로 도망이 정답이다.
        //   밀치기에는 잡아먹히는 개념이 없고, 위험 회피는 PushSurviveState가
        //   무너지는 발판 기준으로 이미 하고 있다.
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            ChangeState(PushSurviveState);
            return;
        }

        if (FindThreat() != null) { ChangeState(FleeState); return; }

        if (FindTargetToChase() != null) { ChangeState(ChaseState); return; }
        ChangeState(WanderState);
    }

    /// <summary>
    /// 플레이어의 이동 속도를 읽어 moveSpeed에 반영한다.
    ///
    /// ★ 어디서 읽는가
    ///   ① 씬에 있는 내 플레이어(PlayerMovement.Local) — 가장 확실하다
    ///   ② 없으면 NetWorld의 플레이어 프리팹(0번) — 관전자·전용 호스트에서도 동작
    ///
    ///   프리팹 값을 읽는 게 핵심이다. 봇 프리팹의 숫자를 손으로 맞추는 방식이면
    ///   플레이어 속도를 조정할 때마다 두 곳을 같이 고쳐야 하고, 언젠가 또 어긋난다.
    /// </summary>
    private void ApplyPlayerSpeed()
    {
        if (!matchPlayerSpeed) return;

        float speed = -1f;

        if (PlayerMovement.Local != null)
        {
            speed = PlayerMovement.Local.moveSpeed;
        }
        else if (NetWorld.Instance != null
                 && NetWorld.Instance.prefabs != null
                 && NetWorld.Instance.prefabs.Length > 0
                 && NetWorld.Instance.prefabs[0] != null)
        {
            PlayerMovement pm = NetWorld.Instance.prefabs[0]
                                    .GetComponentInChildren<PlayerMovement>(true);
            if (pm != null) speed = pm.moveSpeed;
        }

        if (speed <= 0f) return;   // 못 찾으면 인스펙터 값을 그대로 쓴다

        moveSpeed = speed * Mathf.Max(0.1f, speedRatio);
    }

    /// <summary>밀크 등 외부 감속/복원 효과용. 봇의 실제 이동 속도는 Agent.speed인데, 이 값은
    /// FSM 상태 Enter에서만 moveSpeed로부터 갱신된다. 따라서 moveSpeed만 바꾸면 상태 전환이
    /// 일어나기 전까지 이동에 반영되지 않고, 밀크에서 나와도 슬로우가 남는다. 여기서 moveSpeed와
    /// Agent.speed를 같은 비율로 함께 곱해, 상태별 속도 계수(예: Wander 0.9)는 보존하면서 즉시
    /// 반영한다. (이후 상태 Enter가 Agent.speed = moveSpeed로 덮어써도 비율이 일관돼 안전)</summary>
    public void ApplySpeedMultiplier(float multiplier)
    {
        moveSpeed *= multiplier;
        if (Agent != null) Agent.speed *= multiplier;
    }

    // ─────────────────────────────────────────────────────────
    // Update: 현재 상태 업데이트 + 회전 + 애니메이션
    // ─────────────────────────────────────────────────────────
    private void Update()
    {
        //원격 봇의 크기와 위치는 LanBotState·NetTransform이 맞춘다
        if (!IsDriver)
            return;

        // 시작 카운트다운(3-2-1) 동안엔 봇도 멈춘다. 이동/상태 갱신을 건너뛰고 정지 + Idle 유지
        // → 플레이어와 함께 '다 같이 대기 후 시작'.
        // 주의: GameState.Phase가 아니라 카운트다운 전용 플래그를 본다. Phase는 '로컬 플레이어 상태'라,
        //       Absorb에서 호스트 플레이어가 죽으면(Phase=GameOver) 봇이 전부 멈춰 생존자 게임이 깨진다.
        if (IsCountdownActive())
        {
            if (Agent.enabled && Agent.isOnNavMesh)
            {
                if (Agent.hasPath) Agent.ResetPath();
                Agent.velocity = Vector3.zero;
            }
            if (_anim != null) _anim.SetBool("IsMoving", false);
            return;
        }

        if (GameState.CurrentGameMode == GameModeType.Push)
            CheckGroundBelow();

        if (!Agent.enabled || !Agent.isOnNavMesh) return;

        if (_dashCooldownTimer > 0f) _dashCooldownTimer -= Time.deltaTime;
        if (_attackCooldownTimer > 0f) _attackCooldownTimer -= Time.deltaTime;
        if (_dashTimer > 0f)
        {
            _dashTimer -= Time.deltaTime;
            if (_dashTimer <= 0f)
                Agent.speed = _preDashSpeed;
        }

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

        if (isMoving != _wasMoving)
        {
            _wasMoving = isMoving;
        }
    }

    /// <summary>
    /// 시작 카운트다운 중인가. 이 동안엔 봇도 제자리에 선다.
    ///
    /// ★ 주의: GameState.Phase를 보면 안 된다. 그건 '로컬
    ///   플레이어의 상태'라, 흡수 모드에서 호스트 플레이어가 죽으면(GameOver)
    ///   봇이 전부 멈춰 남은 사람들의 게임이 깨진다. 그래서 LanGameFlow의
    ///   진행 단계를 직접 본다.
    /// </summary>
    private bool IsCountdownActive()
    {
        var flow = LanGameFlow.Instance;
        return flow != null && flow.Phase != GamePhase.Playing;
    }

    // ─────────────────────────────────────────────────────────
    // 탐지 함수 (AIDetector에 위임, 상태 클래스 호환용 래퍼)
    // ─────────────────────────────────────────────────────────

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
        //    EntityRegistry는 OnEnable에서 등록된 목록이라 씬 전체를 훑지 않는다
        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || bot == this || bot.IsEliminated) continue;
            if (bot.Agent != null && bot.Agent.enabled && bot.Agent.isOnNavMesh)
            {
                pos = bot.transform.position;
                return true;
            }
        }
        // 2. 살아있는 플레이어
        foreach (var p in EntityRegistry.Players)
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

    private void CheckGroundBelow(bool immediate = false)
    {
        if (!immediate && Time.frameCount % 15 != 0) return;
        if (IsEliminated || IsBeingAbsorbed) return;

        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, 3f)) return;

        if (Agent != null) Agent.enabled = false;
        _currentState?.Exit();
        _currentState = null;

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
        if (!IsDriver) return;

        // Push 모드에서는 흡수(먹기)가 없다. 밀치기/낙사로만 승부.
        if (GameState.CurrentGameMode != GameModeType.Absorb) return;

        // ═════════════════════════════════════════════
        //  [LAN 이식] 봇이 플레이어/봇을 먹는 경로
        // ═════════════════════════════════════════════
        //
        // ★ 봇이 따로 알릴 필요가 없다
        //   호스트가 이미 판정 주체이므로 AbsorbMode에 결과를 넘겨
        //   기존 PlayerAbsorbed 방송 경로를 그대로 태운다.
        //   덕분에 "플레이어가 플레이어를 먹었을 때"와 완전히 같은 코드가 돈다.
        if (_netId != null)
        {
            LanAbsorbTouch(other);
            return;
        }
    }

    /// <summary>
    /// [LAN] 봇이 무언가에 닿았을 때의 흡수 판정. 호스트에서만 돈다.
    ///
    /// 규칙은 원본 그대로다 — 상대가 판 안에 있고, 내가 더 크면 먹는다.
    /// 다른 점은 결과를 내가 쏘지 않고 AbsorbMode에 넘긴다는 것뿐이다.
    /// </summary>
    private void LanAbsorbTouch(Collider other)
    {
        var mode = AbsorbMode.Instance;
        if (mode == null || _netId == null) return;
        if (IsOutOfPlay) return;

        NetIdentity victim = other.GetComponentInParent<NetIdentity>();
        if (victim == null || victim == _netId) return;
        if (victim.PrefabId >= NetConfig.JELLY_PREFAB_START && !victim.IsBot) return; // 젤리는 다른 경로

        float myScale = GetMyAuthorityScale();
        float otherScale = NetEntity.ScaleOf(victim);
        if (otherScale >= myScale) return;

        // 호스트 판정 → 전원에게 방송. 성장도 그 안에서 확정된다.
        mode.HostBotAbsorb(victim.NetId, _netId.NetId);
    }

    /// <summary>
    /// 초콜릿 등으로 탈락 처리. 리더보드/이름표 제거, AI/Agent 정지.
    /// 오브젝트는 파괴하지 않고 둥둥 떠다니게 유지.
    /// 호스트에서 호출되면 BotEliminated 방송으로 전 클라에 전파된다.
    /// </summary>
    public void OnEliminated()
    {
        if (IsEliminated) return;

        // [LAN 이식] 탈락은 호스트가 판정하고 전원에게 알린다.
        //   호스트 자신도 즉시 반영해야 하므로 방송 후 로컬 적용까지 한다.
        //   봇은 전부 NetWorld가 스폰하므로 _netId가 없는 봇은 존재하지 않는다 —
        //   예전엔 그 경우의 폴백이 있었는데, 그쪽은 IsDriver 검사를 건너뛰어서
        //   만약 도달했다면 클라가 봇을 제멋대로 죽일 수 있었다
        if (!IsDriver) return;                       // 클라는 스스로 죽이지 않는다

        // [밀치기] 나를 민 사람이 있으면 내 점수를 넘긴다. 탈락 처리 전에 해야 한다.
        if (PushMode.Instance != null)
            PushMode.Instance.HostAwardKillCredit(_netId.NetId);

        if (_botSync != null) _botSync.HostBroadcastEliminated();
        ApplyEliminatedLocally();
    }

    private void ApplyEliminatedLocally()
    {
        if (IsEliminated) return;
        IsEliminated = true;

        // 초기화 코루틴이 진행 중이면 멈춘다 — 안 그러면 그 코루틴이
        // Agent를 다시 켜고 FSM을 재기동해 방금 탈락시킨 봇이 되살아난다(enabled=false로는 안 멈춤).
        if (_initCoroutine != null) { StopCoroutine(_initCoroutine); _initCoroutine = null; }

        if (Agent != null) Agent.enabled = false;
        _currentState?.Exit();
        _currentState = null;
        enabled = false;

        // 탈락 시 이동 애니메이션 정지 (Update가 멈춰 IsMoving이 true로 남는 것 방지).
        // 모든 클라이언트에서 실행되므로 각자 자기 애니메이터를 끈다.
        if (_anim != null) _anim.SetBool("IsMoving", false);

        if (nameTagBillboard != null) nameTagBillboard.gameObject.SetActive(false);
    }

    /// <summary>[LAN] 호스트가 보낸 탈락 통보. NetWorld가 호출한다.</summary>
    public void ApplyEliminatedFromNet()
    {
        ApplyEliminatedLocally();
    }

    /// <summary>[LAN] 호스트가 확정한 흡수. 전원이 각자 같은 연출을 재생한다.</summary>
    /// <summary>
    /// 흡수 연출 직전에 봇의 두뇌를 멈춘다. 연출 자체는 LanPlayerVisual이 사람과 공용으로 돌린다.
    ///
    /// ★ 예전엔 여기 LanAbsorbedSequence라는 코루틴이 따로 있었다
    ///   사람 쪽 AbsorbedRoutine과 상수까지 같은 20줄이 복사돼 있어서,
    ///   연출을 손보면 사람과 봇이 다르게 빨려 들어갈 수 있었다.
    /// </summary>
    public void StopForAbsorb()
    {
        IsBeingAbsorbed = true;

        //초기화 코루틴이 살아 있으면 Agent를 다시 켜고 FSM을 재기동한다
        if (_initCoroutine != null) { StopCoroutine(_initCoroutine); _initCoroutine = null; }

        if (Agent != null) Agent.enabled = false;

        _currentState?.Exit();
        _currentState = null;
    }

    /// <summary>
    /// 흡수 연출.
    ///
    /// ★ 오브젝트를 없애는 주체
    ///   호스트만 HostDespawn을 부르고, 그 결과가 DespawnEntity로 전파된다.
    ///   각자 지우면 늦게 들어온 사람이 이미 없는 봇을 다시 만들 수 있다.
    /// </summary>
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

        // [LAN 이식] 트리거는 값이 남지 않아 폴링할 수 없다. 쏘는 쪽이 직접 알린다.
        //   플레이어 FSM(PlayerDashState)과 완전히 같은 통로.
        if (_visual != null)
            _visual.SendTrigger(LanPlayerVisual.ANIM_DASH);
        return true;
    }

    // ─────────────────────────────────────────────────────────
    // 공격 (Push 모드) — 시각적 빠따 스윙 + 프레임별 히트 판정
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 배트(빠따)는 Push 모드에서만 활성화한다. 그 외 모드에서는 완전히 숨긴다.
    /// Push 모드에서도 hideBatWhenIdle이면 평상시 숨겨두고 공격 스윙 때만 표시한다.
    /// </summary>
    private void ApplyBatModeVisibility()
    {
        if (batPivot == null) return;
        bool pushMode = GameState.CurrentGameMode == GameModeType.Push;
        batPivot.gameObject.SetActive(pushMode && !hideBatWhenIdle);
    }

    public void TryAttack()
    {
        if (GameState.CurrentGameMode != GameModeType.Push) return;
        if (IsAttacking || _attackCooldownTimer > 0f) return;

        var dm = DataManager.Instance;
        if (dm == null) return;

        _attackCooldownTimer = dm.batCooldown;
        _attackCoroutine = StartCoroutine(AttackSwingRoutine());

        if (_anim != null) _anim.SetTrigger("Attack");

        if (_visual != null)
        {
            _visual.PlayBatSwing();
            _visual.SendTrigger(LanPlayerVisual.ANIM_ATTACK);
        }
    }

    //회전 연출은 LanPlayerVisual.PlayBatSwing이 돌린다(사람·원격과 같은 코드).
    //여기 남는 건 봇에만 있는 일 — 스윙이 도는 동안 명중을 찾는 것뿐이다
    private IEnumerator AttackSwingRoutine()
    {
        var dm = DataManager.Instance;
        if (dm == null) { _attackCoroutine = null; yield break; }

        bool hitDetected = false;
        float elapsed = 0f;

        while (elapsed < dm.batSwingDuration)
        {
            elapsed += Time.deltaTime;

            if (!hitDetected)
                hitDetected = DetectBatHit();

            yield return null;
        }

        _attackCoroutine = null;
    }

    private bool DetectBatHit()
    {
        if (!IsDriver) return false;

        var dm = DataManager.Instance;
        if (dm == null) return false;

        float scale = GetMyAuthorityScale();
        float range = dm.batRange * scale;
        Vector3 origin = transform.position + Vector3.up * (baseAgentHeight * 0.5f * scale);
        float halfArc = dm.batArcAngle * 0.5f;

        int mask = LayerMask.GetMask("Player") | LayerMask.GetMask("Edible");
        Collider[] hits = Physics.OverlapSphere(origin, range, mask);

        foreach (var hit in hits)
        {
            if (hit.transform.root == transform.root) continue;

            Vector3 toTarget = hit.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.001f) continue;

            float angle = Vector3.Angle(transform.forward, toTarget);
            if (angle > halfArc) continue;

            Vector3 pushDir = toTarget.normalized;
            float pushForce = dm.batPushForce * (scale / dm.startingScale);

            // ═════════════════════════════════════════════
            //  [LAN 이식] 넉백 전달을 PushMode에 넘긴다
            // ═════════════════════════════════════════════
            //
            // ★ 왜 직접 안 보내는가
            //   넉백은 맞는 쪽 소유자에게만 가야 한다. 전체에 뿌리면 남의 화면에서도
            //   로컬로 밀려 수신 위치와 충돌해 지터가 난다.
            //   그 규칙이 이미 PushMode.SendKnockback에 있으므로 여기서 다시 만들 이유가 없다.
            //   플레이어가 때렸을 때와 완전히 같은 코드가 돌게 된다.
            if (_netId != null)
            {
                NetIdentity victimId = hit.GetComponentInParent<NetIdentity>();
                if (victimId == null || victimId == _netId) continue;

                var push = PushMode.Instance;
                if (push == null) continue;

                push.HostBotBatHit(victimId.NetId, _netId.NetId);

                float g = dm.batHitGrowth / Mathf.Max(scale, 1f);
                if (ScaleCtrl != null) ScaleCtrl.GrowByBatHit(g);
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────
    // 대쉬 밀치기 (넉백)
    // ─────────────────────────────────────────────────────────

    private Coroutine _knockbackCoroutine;

    /// <summary>[LAN] PushMode가 넉백을 전달한다.</summary>
    public void ApplyKnockbackFromNet(float dirX, float dirZ, float force)
    {
        if (IsEliminated || IsBeingAbsorbed) return;

        if (Agent != null && Agent.isOnNavMesh)
            Agent.ResetPath();

        if (_knockbackCoroutine != null)
            StopCoroutine(_knockbackCoroutine);
        _knockbackCoroutine = StartCoroutine(
            KnockbackRoutine(Knockback.StartVelocity(new Vector3(dirX, 0f, dirZ), force)));
    }

    private IEnumerator KnockbackRoutine(Vector3 startVelocity)
    {
        if (Agent != null) Agent.enabled = false;

        float elapsed = 0f;

        //속도 곡선은 사람과 공유하고, 그 속도로 어떻게 움직일지는 각자 한다.
        //봇은 NavMeshAgent를 끄고 transform을 직접 몬다
        while (Knockback.IsActive(elapsed))
        {
            if (IsEliminated || IsBeingAbsorbed) break;
            transform.position += Knockback.VelocityAt(startVelocity, elapsed) * Time.deltaTime;
            elapsed += Time.deltaTime;
            yield return null;
        }

        _knockbackCoroutine = null;

        if (IsEliminated || IsBeingAbsorbed) yield break;
        if (Agent == null) yield break;

        // 넉백 종료 지점 아래에 땅이 있는지 먼저 확인한다.
        // Agent.enabled = true 는 근처 NavMesh로 자동 스냅시키므로, 맵 외곽으로
        // 밀려났어도 Agent를 다시 켜면 땅으로 복귀해버린다. 그래서 Agent를 켜기 전에
        // 레이캐스트로 발밑 땅을 검사해, 땅이 없으면 그대로 낙하시킨다.
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        bool groundBelow = Physics.Raycast(origin, Vector3.down, 3f);

        if (!groundBelow)
        {
            CheckGroundBelow(true); // 낙하 처리 (Rigidbody 부착)
            yield break;
        }

        Agent.enabled = true;
        if (Agent.isOnNavMesh)
            Agent.Warp(transform.position);
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
