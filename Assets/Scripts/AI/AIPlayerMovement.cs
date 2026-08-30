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
    [SerializeField] private float moveSpeed = 6f;
    public float MoveSpeed { get { return moveSpeed; } set { moveSpeed = value; } }
    [SerializeField] private float rotateSpeed = 10f;

    // ═════════════════════════════════════════════════════════
    //  플레이어와 이동 속도 맞추기
    // ═════════════════════════════════════════════════════════
    //
    // ★ 왜 코드에서 맞추는가
    //   두 프리팹의 값이 조용히 벌어져 있었다.
    //
    //     NetworkPlayer_Bear.MoveSpeed = 18
    //     AIPlayer_Bear.MoveSpeed      =  6      ← 3배 느림
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
    [SerializeField] private bool matchPlayerSpeed = true;

    [Tooltip("플레이어 대비 배율. 1이면 완전히 동일. 봇을 조금 느리게 하려면 0.9 등.")]
    [SerializeField] private float speedRatio = 1f;

    [Header("AI")]
    [SerializeField] private float detectRadius = 15f;
    public float DetectRadius { get { return detectRadius; } }

    // ★ 상태 재평가 주기.
    //   0.4초는 흡수 모드(배회↔추격↔도주)에는 넉넉하지만 밀치기에는 느리다.
    //   밀치기는 PushSurviveState 하나만 쓰므로 재평가가 자주 돌아도 비용이 거의 없다.
    [SerializeField] private float stateEvalRate = 0.15f;

    // ★ 에이전트 기본 크기는 인스펙터에 적지 않는다
    //   예전엔 여기 0.5 / 2.0을 손으로 적어두고 Awake에서 Agent에 밀어 넣었다.
    //   그런데 같은 값이 프리팹의 NavMeshAgent에도 있어서, 한쪽만 고치면 조용히 벌어진다.
    //   에이전트가 진짜 값을 들고 있으므로 거기서 읽어온다 — 출처를 하나로 만든다.
    //   (transform 스케일이 커지면 이 값에 배율을 곱해 Agent에 다시 적는다)
    public float BaseAgentRadius { get; private set; }
    public float BaseAgentHeight { get; private set; }

    [Header("Push 모드 (빠따/대쉬)")]
    [SerializeField] private Transform batPivot;
    public Transform BatPivot { get { return batPivot; } }
    [SerializeField] private bool hideBatWhenIdle = true;
    public bool HideBatWhenIdle { get { return hideBatWhenIdle; } }
    [SerializeField] private float dashSpeed = 80f;
    public float DashSpeed { get { return dashSpeed; } }
    [SerializeField] private float dashDuration = 0.2f;
    public float DashDuration { get { return dashDuration; } }
    [SerializeField] private float dashCooldown = 3f;
    public float DashCooldown { get { return dashCooldown; } }

    [Header("이름표")]
    [SerializeField] private NameTagBillboard nameTagBillboard;

    // ─────────────────────────────────────────────────────────
    // 컴포넌트 (상태 클래스들이 접근)
    // ─────────────────────────────────────────────────────────
    public NavMeshAgent Agent { get; private set; }
    public PlayerScaleController ScaleCtrl { get; private set; }
    public NavMeshQueryFilter NavFilter { get; private set; }
    public NavMeshPath CachedPath { get; private set; }
    public AIDetector Detector { get; private set; }

    private Animator anim;
    private LanPlayerVisual visual;

    // ─────────────────────────────────────────────────────────
    // FSM
    // ─────────────────────────────────────────────────────────
    private AIBaseState currentState;
    private bool isTransitioning = false;

    // 상태 인스턴스 (Start에서 1회 생성, 재사용)
    public AIWanderState WanderState { get; private set; }
    public AIChaseState  ChaseState  { get; private set; }
    public AIFleeState   FleeState   { get; private set; }
    public AIPushSurviveState PushSurviveState { get; private set; }

    private float lastUrgentThreatCheck;
    public bool IsBeingAbsorbed { get; set; } = false;
    public bool IsEliminated { get; private set; } = false;

    /// <summary>봇이 게임에서 빠졌는지(탈락 또는 흡수 진행 중). "이 엔티티가 게임에서 빠졌나?"
    /// 판정의 단일 출처 — 인디케이터/충돌/리더보드가 모두 이 값을 본다. (G6)</summary>
    public bool IsOutOfPlay => IsEliminated || IsBeingAbsorbed;

    private float dashCooldownTimer;
    private float dashTimer;

    // ★ 대쉬 전 속도를 '절대값'으로 기억하면 안 된다
    //   예전엔 preDashSpeed = Agent.speed로 찍어두고 대쉬가 끝나면 그 값을 되돌렸다.
    //   그런데 대쉬 0.4초 사이에 방망이에 맞아 커지거나 밀크를 밟으면 moveSpeed가 바뀐다.
    //   그때 옛날 절대값으로 되돌아가 <b>봇이 엉뚱한 속도로 굳었다.</b>
    //   상태별 계수(예: Wander 0.9)만 기억하고 복귀할 때 moveSpeed에 곱해 다시 계산한다.
    private float stateSpeedRatio = 1f;

    private float attackCooldownTimer;
    private Coroutine attackCoroutine;
    public bool IsDashing => dashTimer > 0f;
    public bool IsAttacking => attackCoroutine != null;


    // ═════════════════════════════════════════════════════════
    //  [LAN 이식] 봇 권위 판정
    // ═════════════════════════════════════════════════════════
    //
    // ★ 봇은 호스트에서만 생각하고, 나머지는 결과만 본다
    //   봇은 전부 호스트 소유 NetIdentity라 그 판정을 IsMine 하나로 표현할 수 있다.
    //
    //   접속이 없으면(오프라인 테스트) 혼자 다 굴린다 — 안 그러면 봇이 얼어붙는다.
    private NetIdentity netId;
    private LanBotState botSync;

    /// <summary>이 기계가 이 봇의 두뇌를 돌리는가.</summary>
    private bool IsDriver
    {
        get
        {
            //봇은 전부 NetWorld가 스폰하므로 netId가 없는 봇은 없다
            return netId != null && netId.IsMineOrOffline;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 외부 프로퍼티
    // ─────────────────────────────────────────────────────────

    /// <summary>이 봇의 netId. LanPlayerState.EntityId와 같은 역할이다.
    /// NetIdentity를 Awake에서 캐시해두므로 매번 계층을 훑지 않는다.</summary>
    public int EntityId => netId != null ? netId.NetId : 0;

    /// <summary>Awake에서 캐시해둔 NetIdentity. LanPlayerState.Identity와 같은 역할이다.</summary>
    public NetIdentity Identity => netId;

    /// <summary>이 봇의 네트워크 상태. Awake에서 캐시해둔 것을 그대로 준다.</summary>
    public LanBotState BotState => botSync;

    /// <summary>
    /// 이 봇의 크기. 판정에 쓰는 값의 출처는 PlayerScaleController 하나다.
    ///
    /// ★ 예전엔 transform.localScale.x를 그대로 돌려줬다
    ///   그건 '지금 화면에 보이는 크기'라, 커지는 연출이 도는 동안(약 0.3초)
    ///   사람 쪽 판정값(CurrentScaleValue = 연출이 끝난 목표 크기)과 어긋났다.
    ///   그 사이 봇은 실제보다 작게 취급돼 흡수당하기 쉬웠다.
    /// </summary>
    public float GetMyAuthorityScale()
    {
        return ScaleCtrl != null ? ScaleCtrl.CurrentScaleValue : transform.localScale.x;
    }

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────
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

        // ★ authoring 값의 주인은 컴포넌트가 아니라 <b>에이전트 타입 설정</b>이다
        //   NavMesh는 타입 설정의 반지름·높이로 구워진다. 프리팹 컴포넌트에도 같은 값이
        //   적혀 있어서 한쪽만 고치면 '구워진 길'과 '실제 에이전트'가 조용히 어긋난다.
        //   지금은 둘이 같지만(0.65 / 1.51) 그걸 코드가 보장하지는 않았다.
        //   타입에서 읽어와 컴포넌트에 그대로 밀어 넣어 어긋날 자리를 없앤다.
        BaseAgentRadius = NavMeshUtil.AgentRadius(Agent.agentTypeID);
        BaseAgentHeight = NavMeshUtil.AgentHeight(Agent.agentTypeID);

        Agent.radius = BaseAgentRadius;
        Agent.height = BaseAgentHeight;

        Detector.Configure(detectRadius, BaseAgentRadius);

        ApplyPlayerSpeed();

        // [수정] NavMeshAgent가 스스로 오브젝트를 이동/회전시키지 못하게 원천 차단
        ApplyStateSpeed();
        Agent.acceleration = 1000f; // 가속도를 극대화하여 즉시 최고속도 도달 (플레이어와 일치)
        Agent.angularSpeed = 0f;
        Agent.stoppingDistance = 0f;
        Agent.autoBraking = false;

        // ★ 이 설정들이 하는 일
        //   Update가 이미 desiredVelocity(순수 희망 방향)를 읽어 Agent.velocity를 직접 세운다.
        //   즉 '가속·감속·회전'은 우리가 하고 에이전트에겐 경로 계산만 맡기는 구조다.
        //   아래 값들은 에이전트가 그 위에 자기 판단을 덧씌우지 못하게 눌러두는 것이다.
        //     acceleration 1000 — 우리가 세운 속도에 도달하는 걸 지연시키지 않는다
        //     angularSpeed 0    — 회전은 Update가 rotateSpeed로 한다
        //     autoBraking off   — 목적지 근처에서 임의로 감속하지 않는다
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
            if (absorber != null)
                absorber.enabled = false;
            // Cloth 제거 (스케일 동기화와 충돌하여 모델 찌그러짐 방지)
            SoftBody3D softBody = GetComponentInChildren<SoftBody3D>();
            if (softBody != null)
                softBody.RemoveCloth();

            Agent.enabled = false;
            return;
        }

        InitAndStart();
    }

    //걸어다닐 수 있는 영역만. 계산은 NavMeshUtil이 한 곳에서 들고 있다

    /// <summary>
    /// NavMesh 위 위치를 확정하고 FSM을 기동한다.
    ///
    /// ★ 예전엔 코루틴이었다 — 이제 그럴 이유가 없다
    ///   "NavMesh가 아직 준비 안 됐을 수 있으니 5초간 0.2초 간격으로 재시도"하는 루프가 있었다.
    ///   그런데 이 프로젝트의 NavMesh는 에디터에서 굽는다(BakeNavMesh는 [ContextMenu] 전용).
    ///   런타임에 새로 생기지 않으므로, 첫 프레임에 실패하면 5초 뒤에도 실패한다.
    ///   오히려 타일이 무너지며 carve로 **줄어들기만** 한다.
    ///
    ///   기다림이 사라지니 "기다리는 동안 탈락했나" 검사 셋도 함께 사라졌다.
    ///   그리고 LanBotSpawner가 스폰 직후 이미 NavMesh에 스냅해두므로,
    ///   여기 남은 일은 '확인'과 '실패 시 폴백'뿐이다.
    /// </summary>
    private void InitAndStart()
    {
        Agent.enabled = false;

        NavFilter = new NavMeshQueryFilter
        {
            agentTypeID = Agent.agentTypeID,
            areaMask    = NavMeshUtil.WalkableMask
        };

        if (!TryPlaceOnNavMesh())
        {
            Debug.LogWarning($"[AIBot] {name} 를 NavMesh에 올리지 못했습니다 — Agent를 끈 채로 둡니다.");
            return;
        }

        Agent.enabled = true;

        if (!Agent.isOnNavMesh)
        {
            Debug.LogWarning($"[AIBot] {name} NavMesh 배치 실패 (위치 {transform.position})");
            return;
        }

        ApplyAvoidancePriority(GetMyAuthorityScale());

        ChangeState(GameState.CurrentGameMode == GameModeType.Push ? PushSurviveState : WanderState);
        StartCoroutine(StateEvalLoop());
    }

    /// <summary>지금 자리 또는 폴백 자리에서 NavMesh 위로 옮긴다.</summary>
    private bool TryPlaceOnNavMesh()
    {
        //스포너가 이미 스냅해뒀으므로 보통 여기서 끝난다.
        //반경은 스포너와 같은 10m — 예전엔 100m라 실패한 봇이 맵 반대편으로 끌려갔다
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavFilter))
        {
            transform.position = hit.position;
            return true;
        }

        if (!TryFindFallbackSpawnPos(out Vector3 fallback))
            return false;

        Debug.LogWarning($"[AIBot] {name} 초기 위치 {transform.position} 에서 NavMesh 미발견 → 폴백 {fallback}");

        if (!NavMesh.SamplePosition(fallback, out NavMeshHit fhit, 10f, NavFilter))
            return false;

        transform.position = fhit.position;
        return true;
    }

    // ─────────────────────────────────────────────────────────
    // 상태 관리 (PlayerController.ChangeState 패턴)
    // ─────────────────────────────────────────────────────────

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

    /// <summary>주기적으로 상태 전환 평가 (우선순위: Flee > Chase > Wander)</summary>
    private IEnumerator StateEvalLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(stateEvalRate);

            // 정상적으로 추락 중인 봇은 Agent가 꺼져 있다(CheckGroundBelow/AwakePhysicsOnTile).
            // 그런 봇은 물리로 떨어지게 그대로 둔다.
            if (!Agent.enabled)
                continue;

            // Agent는 켜져 있는데 NavMesh 밖에 있는 봇 = 넉백/붕괴로 발판 없는 허공에 박제된 경우.
            //
            // ★ 무너진 발판 위에 있었으면 '구조'가 아니라 '낙하'가 맞다
            //   예전엔 흡수 모드에서 무조건 가장 가까운 안전 타일로 Warp했다.
            //   그래서 발판과 함께 초콜릿으로 떨어져야 할 봇이, FallingTile의
            //   OverlapBox에 안 잡히기만 하면 슬그머니 땅 위로 되돌아왔다
            //   ("봇이 초콜릿에 안 들어가고 땅에 남아 있다"의 정체).
            //   먼저 발밑을 재서 진짜 허공이면 떨어뜨리고, 발판이 있는데 NavMesh만
            //   어긋난 경우(유령 NavMesh)에만 예전처럼 안전 타일로 복귀시킨다.
            if (!Agent.isOnNavMesh)
            {
                if (CheckGroundBelow(true))
                    continue;   //발밑이 비었다 → 물리 낙하

                //① 안전 타일로 되돌린다(무너지지 않은 칸을 골라주므로 가장 좋다)
                var c = TileCollapseManager.Instance;
                if (c != null && c.FindNearestSafeTile(transform.position, out Vector3 offSafe)
                    && NavMesh.SamplePosition(offSafe, out NavMeshHit offHit, 10f, NavFilter))
                {
                    RecoverTo(offHit.position);
                    continue;
                }

                // ② 안전 타일을 못 찾았을 때의 폴백 — 맵 외곽에서 실제로 걸린다.
                //   여기까지 오면 예전엔 그냥 continue 라서, 발밑에 콜라이더는 있고
                //   NavMesh만 없는 자리에 <b>영영 박제</b>됐다.
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit nearHit, 3f, NavFilter))
                {
                    RecoverTo(nearHit.position);
                    continue;
                }

                //③ 어디로도 못 돌아간다 = 설 곳이 없는 자리다. 서 있지 말고 떨어뜨린다
                AwakeFallPhysics();
                continue;
            }

            // 발판이 없는 허공(붕괴된 타일 자리의 잔존 NavMesh 등) 위에 떠 있으면
            // 먼저 떨어뜨려 본다. 발판이 남아 있는데 그리드만 붕괴로 표시된 경우에는
            // 예전처럼 가장 가까운 안전 타일로 복귀시켜 공중 박제를 푼다.
            var collapse = TileCollapseManager.Instance;
            if (collapse != null && collapse.IsOverVoid(transform.position))
            {
                if (CheckGroundBelow(true))
                    continue;   //발밑이 비었다 → 물리 낙하

                if (collapse.FindNearestSafeTile(transform.position, out Vector3 safeTile)
                    && (NavMesh.SamplePosition(safeTile, out NavMeshHit voidHit, 10f, NavFilter)))
                {
                    Agent.Warp(voidHit.position);
                    // 복귀 직후엔 허공을 향하던 기존 경로가 무효이므로 비우고, 다음 평가 때
                    // 모드에 맞는 상태(Wander/PushSurvive 등)가 새 목적지를 잡게 한다.
                    if (Agent.isOnNavMesh && Agent.hasPath)
                        Agent.ResetPath();
                }
                continue;
            }

            if (GameState.CurrentGameMode == GameModeType.Push)
            {
                if (Detector.FindThreat() != null)
                {
                    ChangeState(FleeState);
                    continue;
                }
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
        //   예전엔 위협 검사가 먼저였다. 그런데 밀치기에서는 배트를 맞히면 커지므로,
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
            return;   // 못 찾으면 인스펙터 값을 그대로 쓴다

        moveSpeed = speed * Mathf.Max(0.1f, speedRatio);
    }

    /// <summary>밀크 등 외부 감속/복원 효과용. 봇의 실제 이동 속도는 Agent.speed인데, 이 값은
    /// FSM 상태 Enter에서만 moveSpeed로부터 갱신된다. 따라서 moveSpeed만 바꾸면 상태 전환이
    /// 일어나기 전까지 이동에 반영되지 않고, 밀크에서 나와도 슬로우가 남는다. 여기서 moveSpeed와
    /// Agent.speed를 같은 비율로 함께 곱해, 상태별 속도 계수(예: Wander 0.9)는 보존하면서 즉시
    /// 반영한다. (이후 ApplyStateSpeed가 moveSpeed × 계수로 다시 계산해도 비율이 일관돼 안전)</summary>
    public void ApplySpeedMultiplier(float multiplier)
    {
        moveSpeed *= multiplier;
        if (Agent != null)
            Agent.speed *= multiplier;
    }

    /// <summary>
    /// 상태가 자기 이동 속도를 적용한다. FSM 상태의 Enter는 Agent.speed를 직접 쓰지 말고 이걸 부른다.
    ///
    /// ★ 왜 통로를 하나로 모으는가
    ///   예전엔 상태마다 <c>ai.Agent.speed = ai.MoveSpeed</c>를 직접 썼다(9곳).
    ///   대쉬 중에 상태가 바뀌면 그 한 줄이 대쉬 속도를 지워버리는데 dashTimer는 계속 돌아서,
    ///   <b>"대쉬 중(IsDashing=true)인데 걷는 속도"</b>인 구간이 최대 dashDuration만큼 생겼다.
    ///   AIPushSurviveState.Update는 첫 줄에서 IsDashing이면 돌아가므로 그동안 새 목적지도 못 받는다.
    ///   여기서 대쉬 중이면 덮어쓰지 않고 계수만 기억해두었다가, 대쉬가 끝날 때 그 계수로 복귀한다.
    /// </summary>
    /// <param name="ratio">상태별 속도 계수. 예: 배회는 0.9.</param>
    public void ApplyStateSpeed(float ratio = 1f)
    {
        stateSpeedRatio = ratio;

        if (Agent != null && !IsDashing)
            Agent.speed = moveSpeed * ratio;
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
                if (Agent.hasPath)
                    Agent.ResetPath();
                Agent.velocity = Vector3.zero;
            }
            if (anim != null)
                anim.SetBool(AnimParams.IsMoving, false);
            return;
        }

        //예전엔 Push 모드에서만 발밑을 봤다. 흡수 모드에도 무너지는 발판과 초콜릿 강이
        //있으므로 모드를 가릴 이유가 없다 — 발판이 사라졌으면 어느 모드든 떨어져야 한다.
        //(호출부가 IsDriver로 이미 걸러져 있어 클라는 남의 봇에 물리를 붙이지 않는다)
        if (CheckGroundBelow())
            return;

        // ★ 시간 계산은 NavMesh 밖에 있어도 돌아야 한다
        //   예전엔 이 블록이 아래 isOnNavMesh 가드 <b>뒤</b>에 있었다.
        //   대쉬 도중 발판이 무너져 NavMesh를 벗어나면 dashTimer가 그 자리에서 멈추고,
        //   IsDashing이 영원히 true가 된다. AIPushSurviveState.Update는 첫 줄에서
        //   `if (ai.IsDashing) return;` 으로 돌아가므로 <b>새 목적지를 영영 못 받는다</b>
        //   → NavMesh로 복귀시켜줘도 그 자리에 가만히 서 있다가 죽었다.
        //   타이머는 위치와 무관한 순수 시간 계산이니 가드보다 먼저 돌린다.
        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;
        if (dashTimer > 0f)
        {
            dashTimer -= Time.deltaTime;

            //스냅샷이 아니라 재계산이다 — 대쉬 도중 커지거나 밀크를 밟아
            //moveSpeed가 바뀌었어도 그 최신 값 위에 상태 계수만 얹는다
            if (dashTimer <= 0f && Agent != null)
                Agent.speed = moveSpeed * stateSpeedRatio;
        }

        //NavMesh 이탈 복구는 StateEvalLoop이 한 곳에서 맡는다(0.15초마다).
        //여기서 또 하면 같은 일을 두 곳에서 하게 된다
        if (!Agent.enabled || !Agent.isOnNavMesh)
            return;

        // 현재 상태 Update (목적지 설정 등)
        currentState?.Update();

        // 긴급 위협 감지: FleeState가 아닐 때 0.1초 간격으로 위협 체크
        if (currentState != FleeState
            && Time.time - lastUrgentThreatCheck >= 0.1f)
        {
            lastUrgentThreatCheck = Time.time;
            if (Detector.FindThreat() != null)
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
            transform.rotation = SmoothDamping.RotateTowards(
                transform.rotation, wishDir, rotateSpeed, Time.deltaTime);
        }

        // 애니메이터 처리
        bool isMoving = Agent.velocity.magnitude > 0.1f;
        if (anim != null)
            anim.SetBool(AnimParams.IsMoving, isMoving);
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

    // 탐지는 전부 AIDetector가 한다. 예전엔 여기 같은 이름의 위임 래퍼가 네 개 있었는데
    // 로직이 한 줄도 없으면서 "탐지가 이상하다 → 여기 열어봄 → 또 다른 파일로 점프"만
    // 만들었다. Detector가 이미 public이라 감싸서 얻는 것도 없었다.

    /// <summary>
    /// 스폰 위치가 NavMesh로부터 멀리 떨어진 경우 폴백 위치 탐색.
    /// 우선순위: 살아있는 다른 봇(NavMesh 위) → 살아있는 플레이어 → NavMesh 삼각망 정점
    /// </summary>
    // 폴백 자리를 잡을 때 원본 좌표에서 흩어놓는 반경(m).
    // 그대로 쓰면 여러 봇이 같은 자리에 겹쳐 스폰돼 서로 밀어내며 튄다
    private const float FallbackScatterRadius = 4f;

    /// <summary>
    /// 스폰 위치가 NavMesh에서 벗어났을 때 대신 쓸 자리.
    /// 기준점을 찾은 뒤 그 주변으로 흩어 실제 NavMesh 위 좌표를 돌려준다.
    /// </summary>
    private bool TryFindFallbackSpawnPos(out Vector3 pos)
    {
        //1. NavMesh 위에 있다고 '검증된' 다른 봇 — 여긴 봇만 볼 수밖에 없다.
        //   사람에겐 NavMeshAgent가 없어서 그 자리가 NavMesh 위인지 확인할 방법이 없다.
        //   그래서 봇을 먼저 보고, 없을 때만 사람 자리를 쓴다.
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || !e.IsBot || e.Transform == transform || e.IsOutOfPlay)
                continue;

            AIPlayerMovement other = e.Identity != null ? e.Identity.Bot : null;
            if (other != null && other.Agent != null && other.Agent.enabled && other.Agent.isOnNavMesh)
                return ScatterNear(e.Transform.position, out pos);
        }

        //2. 살아있는 사람 플레이어
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.IsBot || e.Transform == null || e.IsOutOfPlay)
                continue;
            return ScatterNear(e.Transform.position, out pos);
        }

        //3. NavMesh 삼각망 정점 — 여긴 이미 서로 떨어져 있으므로 흩을 필요가 없다
        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices != null && tri.vertices.Length > 0)
        {
            pos = tri.vertices[Random.Range(0, tri.vertices.Length)];
            return true;
        }

        pos = Vector3.zero;
        return false;
    }

    /// <summary>기준점 주변 NavMesh 위의 임의 지점. 실패하면 기준점 그대로.</summary>
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

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 20f, NavFilter))
            {
                if (collapse != null && collapse.IsPositionDangerous(hit.position))
                    continue;
                destination = hit.position;
                return true;
            }
        }

        // 최후 수단: 앞 방향 소폭 이동
        Vector3 fwd = transform.position + transform.forward * 5f;
        if (NavMesh.SamplePosition(fwd, out NavMeshHit fwHit, 10f, NavFilter))
        {
            destination = fwHit.position;
            return true;
        }

        destination = transform.position;
        return false;
    }

    // ─────────────────────────────────────────────────────────
    // 발 밑 지면 확인 → 없으면 낙하 (Push·흡수 공통)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 발밑에 지면이 없으면 Agent를 끄고 Rigidbody를 붙여 물리로 떨어뜨린다.
    /// </summary>
    /// <returns>이번 호출로 낙하가 시작됐거나 이미 낙하 중이면 true.</returns>
    //봇마다 다른 값(인스턴스 ID 기반)이라 검사 프레임이 서로 어긋난다
    private int GroundCheckPhase => Mathf.Abs(GetInstanceID()) % GroundCheckInterval;
    private const int GroundCheckInterval = 15;

    // ★ 왜 피벗이 아니라 '발밑'에서 쏘는가 (커진 봇이 멀쩡한 타일 밑으로 꺼지던 원인)
    //
    //   예전엔 이랬다:
    //       Vector3 origin = transform.position + Vector3.up * 0.5f;
    //       if (Physics.Raycast(origin, Vector3.down, 3f)) return false;
    //
    //   봇의 피벗은 캡슐 <b>중심</b>(center 0,0,0)이고, NavMeshAgent가 baseOffset(0.67)만큼
    //   띄워서 세운다. 봇이 커지면 Agent.radius·height는 스케일을 따라 키우는데
    //   (ApplyScaleToAgent) baseOffset은 그대로고, 여기 0.5m·3m도 상수였다.
    //   그래서 피벗이 바닥에서 2.5m 넘게 올라가는 순간 레이가 타일에 닿지 못했고,
    //   "발밑이 비었다"로 단정해 AwakeFallPhysics를 불렀다 —
    //   <b>멀쩡한 타일 아래로 쑥 꺼져서 탈락.</b> 커진 봇에게만 갑자기 생기던 증상이다.
    //
    //   그래서 <b>출발점은 피벗, 길이는 몸 길이에 비례</b>로 잡는다.
    //   피벗은 baseOffset 덕에 항상 지면 위에 있으니 레이가 바닥 안에서 시작할 일이 없고,
    //   길이는 '피벗에서 발바닥까지' + 여유라서 봇이 커져도 늘 발밑까지 닿는다.
    //   (발바닥의 출처는 콜라이더 bounds — TileCollapseManager의 접지 판정과 같은 기준이다)
    //
    //   마스크도 없었다. 그대로 두면 초콜릿 강의 트리거 콜라이더 같은 것도 '지면'으로
    //   쳐서, 반대로 떨어져야 할 때 안 떨어지는 길이 열려 있었다.
    private const float GroundRayLift = 0.5f;    // 피벗에서 이만큼 더 위에서 쏜다
    private const float GroundRayReach = 2.5f;   // 발바닥 아래로 이만큼까지 지면을 찾는다

    private Collider bodyCollider;

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
        //★ 매 프레임 레이캐스트를 쏘지 않으려는 간격 조절이다.
        //  15프레임에 한 번(60fps에서 초당 4회)만 실제로 검사하고 나머지는 그냥 돌아간다.
        //  immediate는 "지금 당장 답이 필요하다"는 뜻 — 타일이 무너진 직후나
        //  넉백이 끝난 직후엔 기다릴 수 없으므로 간격을 건너뛴다.
        //
        //  나머지 항(GroundCheckPhase)이 없으면 모든 봇이 같은 프레임에 몰려 쏜다.
        //  봇마다 어긋나게 해서 비용을 프레임에 고루 편다
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

        if (Physics.Raycast(origin, Vector3.down, rayLength,
                            GameLayers.StandableMask, QueryTriggerInteraction.Ignore))
            return false;

        AwakeFallPhysics();
        return true;
    }

    /// <summary>
    /// NavMesh 위 한 지점으로 되돌린다.
    /// 옮긴 뒤 기존 경로는 허공을 향하고 있으므로 반드시 비운다 —
    /// 안 비우면 다음 평가까지 옛 경로를 그대로 따라가려 한다.
    /// </summary>
    private void RecoverTo(Vector3 pos)
    {
        Agent.Warp(pos);

        if (Agent.isOnNavMesh && Agent.hasPath)
            Agent.ResetPath();
    }

    /// <summary>
    /// 길찾기를 끄고 물리로 떨어뜨린다. 발밑이 비었을 때의 유일한 출구.
    ///
    /// StopBrain은 FSM 상태를 닫는 일까지 한다 — 상태 객체는 이 클래스 안에만 있어서
    /// PhysicsFall이 대신할 수 없는 유일한 조각이다.
    /// NavMeshAgent·CharacterController를 끄고 Rigidbody를 깨우는 건 PhysicsFall이 한다.
    /// (예전엔 여기서도 Agent.enabled = false를 한 번 더 했다)
    /// </summary>
    private void AwakeFallPhysics()
    {
        StopBrain();
        PhysicsFall.Begin(gameObject);
    }

    // ─────────────────────────────────────────────────────────
    // 회피 우선순위
    // ─────────────────────────────────────────────────────────
    //
    // NavMeshAgent.avoidancePriority는 0~99이고 <b>숫자가 낮을수록 우선순위가 높다.</b>
    // 우선순위가 높은 쪽은 회피 계산에서 무시당하지 않고, 낮은 쪽이 알아서 비켜준다.
    // 그래서 "큰 젤리가 밀고 지나가고 작은 애들이 비킨다"를 만들려면 크면서 숫자를 낮춰야 한다.
    //
    // ★ 예전엔 크기가 바뀔 때마다 5씩 깎았다 — 세 가지가 어긋나 있었다
    //   ① 크기가 아니라 '크기가 바뀐 횟수'를 셌다. 젤리 하나를 먹어도 -5, 두 배로 커져도 -5.
    //   ② 줄어들 때도 -5였다. OnPostScalePhysics는 ScaleTo 코루틴 끝에서 나오는데
    //      그 코루틴은 성장과 축소를 모두 탄다 → 밀크로 작아진 봇의 우선순위가 올라갔다.
    //   ③ Mathf.Max(0, …)로 바닥이 막혀 있고 되돌리는 코드가 없어, 20으로 시작한 봇은
    //      네 번이면 0에 붙박였다. 그때부터 스폰 때 흩어놓은 값도 의미가 없어진다.
    //
    //   지금은 현재 크기에서 매번 새로 계산한다. 누적이 없으니 축소도 저절로 맞고,
    //   크기가 같으면 값도 같아진다.
    private const int BasePriority = 50;      // 시작 크기(1배)일 때
    private const float PriorityPerScale = 10f; // 1배 커질 때마다 낮출 양

    //크기가 같은 봇끼리 값이 완전히 같으면 서로 비켜주다 교착된다. 봇마다 조금씩 어긋내둔다
    private int avoidanceJitter;

    private void ApplyAvoidancePriority(float scale)
    {
        if (Agent == null)
            return;

        int p = BasePriority - Mathf.RoundToInt((scale - 1f) * PriorityPerScale) + avoidanceJitter;
        Agent.avoidancePriority = Mathf.Clamp(p, 0, 99);
    }

    // ─────────────────────────────────────────────────────────
    // 크기 변화 뒤처리 (PlayerScaleController → BotBridge → 여기)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 몸집이 바뀐 뒤 NavMeshAgent를 새 크기에 맞춰 다시 맞춘다.
    /// 크기를 바꾸는 게 아니라, 이미 바뀐 크기를 에이전트에 반영하는 쪽이다.
    /// (캡슐 크기 · 회피 우선순위 · NavMesh 재착지)
    /// </summary>
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

        //판정에 쓰는 크기를 그대로 쓴다(연출이 끝난 뒤라 transform과 같지만, 출처를 하나로 둔다)
        float s = GetMyAuthorityScale();

        // 에이전트 캡슐 크기 갱신
        Agent.radius = BaseAgentRadius * s;
        Agent.height = BaseAgentHeight * s;
        ApplyAvoidancePriority(s);

        //봇은 agentTypeID가 0이라 NavFilter와 AllAreas 오버로드의 결과가 같다.
        //예전엔 둘을 || 로 이어 폴백처럼 썼지만 실패 조건이 동일해 죽은 코드였다
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f * s, NavFilter))
            Agent.Warp(hit.position);
    }

    // ─────────────────────────────────────────────────────────
    // 흡수 처리 (플레이어 ↔ AI)
    // ─────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!IsDriver)
            return;

        // Push 모드에서는 흡수(먹기)가 없다. 밀치기/낙사로만 승부.
        if (GameState.CurrentGameMode != GameModeType.Absorb)
            return;


        // ═════════════════════════════════════════════
        //  [LAN 이식] 봇이 플레이어/봇을 먹는 경로
        // ═════════════════════════════════════════════
        //
        // ★ 봇이 따로 알릴 필요가 없다
        //   호스트가 이미 판정 주체이므로 AbsorbMode에 결과를 넘겨
        //   기존 PlayerAbsorbed 방송 경로를 그대로 태운다.
        //   덕분에 "플레이어가 플레이어를 먹었을 때"와 완전히 같은 코드가 돈다.
        if (netId != null)
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
        if (mode == null || netId == null)
            return;
        if (IsOutOfPlay)
            return;

        NetIdentity victim = other.GetComponentInParent<NetIdentity>();
        if (victim == null || victim == netId)
            return;
        //젤리는 이 경로가 아니다 — PlayerAbsorber → JellyColliderAbsorb 연출 →
        //젤리 스스로 AbsorbMode.RequestEat 을 보내는 별도 왕복으로 처리된다.
        //여기서 잡으면 젤리에 사람 탈락 경로(플래그·관전·킬 크레딧)가 돌고, 이중 흡수가 된다
        if (NetEntity.IsJelly(victim))
            return;

        // ★ 상대 캐릭터는 트리거 콜라이더를 둘 갖고 있다 — 대표 하나만 받는다
        //   안 걸면 같은 상대에게 흡수 판정이 두 번 돈다. IsOutOfPlay 가드가 두 번째를
        //   막아주긴 하지만 그건 사후 방어지 규칙이 아니다. 여기서 한 번으로 못 박는다.
        if (!GameTags.IsCharacterMainCollider(other))
            return;

        float myScale = GetMyAuthorityScale();
        float otherScale = NetEntity.ScaleOf(victim);
        if (otherScale >= myScale)
            return;

        // 호스트 판정 → 전원에게 방송. 성장도 그 안에서 확정된다.
        mode.HostBotAbsorb(victim.NetId, netId.NetId);
    }

    /// <summary>
    /// 초콜릿 등으로 탈락했다고 신고한다. 모든 기계에서 불리지만 판정은 호스트만 한다.
    ///
    /// ★ IsEliminated를 여기서 세우면 안 된다
    ///   이 함수는 클라에서도 불린다(ChocolateFluid.OnTriggerEnter는 각자 돈다).
    ///   그런데 클라는 아래 IsDriver 검사에서 되돌아가고, 실제 탈락은 호스트가 보내는
    ///   BotEliminated를 받아 ApplyEliminated로 처리한다.
    ///   여기서 플래그를 먼저 세워버리면 클라에서는
    ///     ① 정리(Agent 정지·이름표 제거)는 하나도 안 한 채 '탈락됨'으로 표시되고
    ///     ② 나중에 진짜 통보가 와도 ApplyEliminated의 첫 줄에 걸려 통째로 무시된다
    ///   → 봇이 죽은 셈 치는데 계속 걸어다니는 유령이 된다.
    ///   플래그는 '정리를 실제로 끝냈다'는 뜻이어야 하므로 ApplyEliminated가 세운다.
    ///   호스트에서 두 번 불려도 그 사이가 전부 동기 호출이라 중복 정산은 없다.
    /// </summary>
    public void ReportEliminated()
    {
        if (IsEliminated)
            return;

        //봇은 전부 NetWorld가 스폰하므로 netId가 없는 봇은 존재하지 않는다 —
        //예전엔 그 경우의 폴백이 있었는데, 그쪽은 IsDriver 검사를 건너뛰어서
        //만약 도달했다면 클라가 봇을 제멋대로 죽일 수 있었다
        if (!IsDriver)
            return;                       // 클라는 스스로 죽이지 않는다

        //킬 크레딧 정산·방송·로컬 적용은 전부 이 관문 안에서 일어난다.
        //사람의 탈락(LanGameFlow.HostConfirmEliminated)도 같은 문으로 들어간다
        NetEntity.HostEliminate(netId);
    }

    /// <summary>[LAN] 탈락을 실제로 적용한다. 호스트는 NetEntity를 거쳐, 클라는 통보를 받아 부른다.</summary>
    public void ApplyEliminated()
    {
        if (IsEliminated)
            return;
        IsEliminated = true;

        StopBrain();
        enabled = false;

        // 탈락 시 이동 애니메이션 정지 (Update가 멈춰 IsMoving이 true로 남는 것 방지).
        // 모든 클라이언트에서 실행되므로 각자 자기 애니메이터를 끈다.
        if (anim != null)
            anim.SetBool(AnimParams.IsMoving, false);

        if (nameTagBillboard != null)
            nameTagBillboard.gameObject.SetActive(false);
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
        StopBrain();
    }

    /// <summary>
    /// 이 봇의 두뇌를 멈춘다 — 길찾기를 끄고 FSM에서 빠져나온다.
    /// 판에서 빠지는 두 경로(탈락·흡수)가 같은 정지 절차를 쓴다는 뜻이다.
    /// 무엇을 '더' 하느냐만 각자 다르다(탈락은 컴포넌트까지 끄고, 흡수는 연출이 이어진다).
    /// </summary>
    private void StopBrain()
    {
        if (Agent != null)
            Agent.enabled = false;

        currentState?.Exit();
        currentState = null;
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

        // [LAN 이식] 트리거는 값이 남지 않아 폴링할 수 없다. 쏘는 쪽이 직접 알린다.
        //   플레이어 FSM(PlayerDashState)과 완전히 같은 통로.
        if (visual != null)
            visual.SendTrigger(LanPlayerVisual.ANIM_DASH);
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

        //휘두르는 연출은 사람과 같은 코드를 쓴다
        BatSwing.Play(transform, anim, visual, GetMyAuthorityScale());
    }

    //회전 연출은 LanPlayerVisual.PlayBatSwing이 돌린다(사람·원격과 같은 코드).
    //여기 남는 건 봇에만 있는 일 — 스윙이 도는 동안 명중을 찾는 것뿐이다
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

    /// <summary>
    /// 스윙 궤적 안의 상대를 찾아 호스트 판정을 확정한다. 호스트에서만 돈다.
    ///
    /// ★ 판정 자체는 BatArcQuery가 한다 — 사람과 같은 코드다
    ///   예전엔 여기와 PlayerAttackState에 같은 판정이 두 벌 있었고,
    ///   젤리 제외와 <b>판 밖 상대 제외가 사람 쪽에만</b> 있었다.
    ///   그래서 봇은 초콜릿에 빠진 시체를 계속 때렸다.
    /// </summary>
    private bool DetectBatHit()
    {
        if (!IsDriver || netId == null)
            return false;

        DataManager dm = DataManager.Instance;
        PushMode push = PushMode.Instance;

        if (dm == null || push == null)
            return false;

        float scale = GetMyAuthorityScale();
        NetIdentity victim = BatArcQuery.Find(transform, netId, scale);

        if (victim == null)
            return false;

        // ★ 넉백을 직접 보내지 않고 PushMode에 넘긴다
        //   넉백은 맞는 쪽 소유자에게만 가야 한다. 전체에 뿌리면 남의 화면에서도
        //   로컬로 밀려 수신 위치와 충돌해 지터가 난다.
        //   그 규칙이 이미 PushMode.SendKnockback에 있다 —
        //   플레이어가 때렸을 때와 완전히 같은 코드가 돈다.
        push.HostBotBatHit(victim.NetId, netId.NetId);

        if (ScaleCtrl != null)
            ScaleCtrl.GrowByBatHit(dm.BatHitGrowth / Mathf.Max(scale, 1f));

        return true;
    }

    // ─────────────────────────────────────────────────────────
    // 대쉬 밀치기 (넉백)
    // ─────────────────────────────────────────────────────────

    private Coroutine knockbackCoroutine;

    /// <summary>[LAN] PushMode가 넉백을 전달한다.</summary>
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

        //속도 곡선은 사람과 공유하고, 그 속도로 어떻게 움직일지는 각자 한다.
        //봇은 NavMeshAgent를 끄고 transform을 직접 몬다
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

        //넉백 동안 Agent를 끄고 transform만 옮겼으므로 에이전트 내부 좌표는 맞기 전 자리에 있다.
        //Warp로 다시 맞춰주지 않으면 다음 프레임에 옛 자리로 되돌아간다
        Agent.enabled = true;

        if (Agent.isOnNavMesh)
        {
            Agent.Warp(transform.position);
            yield break;
        }

        //★ 땅이 있다고 NavMesh가 있는 건 아니다
        //  무너지는 발판은 carve로 NavMesh에서 파여도 콜라이더는 남는다.
        //  예전엔 여기서 그냥 끝나서, Agent는 켜졌는데 NavMesh 밖이라
        //  Update의 isOnNavMesh 검사에 매 프레임 걸려 봇이 그 자리에 얼어붙었다.
        //  (여기서 CheckGroundBelow를 부르는 건 소용이 없다 — 발밑에 땅이 있는 건
        //   위에서 이미 확인했으므로 그 함수는 아무 일도 하지 않고 돌아간다)
        Agent.enabled = false;

        if (TryPlaceOnNavMesh())
        {
            Agent.enabled = true;
            if (Agent.isOnNavMesh)
                Agent.Warp(transform.position);
            yield break;
        }

        Debug.LogWarning($"[AIBot] {name} 넉백 후 NavMesh 복귀 실패 — Agent를 끈 채로 둡니다.");
    }

    // ─────────────────────────────────────────────────────────
    // 디버그
    // ─────────────────────────────────────────────────────────
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
