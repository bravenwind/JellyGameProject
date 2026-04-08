// ============================================================
// AIPlayerMovement.cs
// ============================================================
// 역할: NavMeshAgent 기반 AI 이동 + 5-state FSM
//
// FSM 상태:
//   Wander          - 랜덤 배회
//   Chase           - 젤리 추적
//   Flee            - 위협(나보다 큰 상대) 도망
//   ScaleIncreasing - 크기 증가 중
//   ScaleDecreasing - 크기 감소 중
//
// AIBot 프리팹 설정:
//   - NavMeshAgent 컴포넌트 추가 (CharacterController 제거)
//   - PlayerController 비활성화
//   - PlayerAbsorber.isBot = true
//   - PlayerAbsorbingManager.isBot = true
//   - PlayerScaleController.isBot = true
//   - PlayerColorVisual.isBot = true
//
// TODO : FindObjectsByType 대신 젤리 리스트 직접 참조
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

public enum AIState
{
    Wander, Chase, Flee, ScaleIncreasing, ScaleDecreasing
}

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PhotonView))]
public class AIPlayerMovement : MonoBehaviourPun
{
    [Header("이동")]
    public float moveSpeed = 6f;
    public float rotateSpeed = 10f;

    [Header("AI")]
    public float detectRadius = 15f;
    public float pathRefreshRate = 0.4f;

    [Header("NavMeshAgent 기본 크기 (스케일 1 기준)")]
    public float baseAgentRadius = 0.5f;
    public float baseAgentHeight = 2.0f;

    [Header("FSM (읽기 전용)")]
    [SerializeField] private AIState _currentState = AIState.Wander;
    public AIState CurrentState => _currentState;

    // ── 내부 ──
    private NavMeshAgent _agent;
    private PlayerScaleController _scaleCtrl;
    private NavMeshQueryFilter _navFilter;

    // 스케일 상태 복귀용
    private AIState _stateBeforeScale = AIState.Wander;
    private int _prevScaleLevel = 1;

    // 이동 상태 추적
    private Transform   _chaseTarget;
    private Vector3     _wanderTarget;
    private bool        _hasWanderTarget  = false;
    private float       _lastSetDestTime  = -10f; // SetPath 호출 시간 (pathFailed 오감지 방지)
    private float       _chasePathTimer   = 0f;
    private const float CHASE_PATH_RATE   = 0.15f; // Chase 경로 갱신 간격
    private NavMeshPath _cachedPath;               // GC 방지: 재사용

    // ── 디버그 (Inspector에서 실시간 확인) ──
    [Header("디버그 (읽기 전용)")]
    [SerializeField] private bool   _dbg_isOnNavMesh;
    [SerializeField] private bool   _dbg_hasPath;
    [SerializeField] private float  _dbg_remainingDist;
    [SerializeField] private float  _dbg_velocity;
    // Wander
    [SerializeField] private float  _dbg_distToWanderTarget;
    [SerializeField] private bool   _dbg_hasWanderTarget;
    [SerializeField] private string _dbg_wanderSkipReason = "-";
    // Chase
    [SerializeField] private bool   _dbg_chaseTargetExists;
    [SerializeField] private float  _dbg_distToChaseTarget;
    [SerializeField] private string _dbg_chasePathStatus   = "-";
    // 멈춤 감지용
    private float _stopTimer = 0f;
    private const float STOP_LOG_THRESHOLD = 0.5f;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _scaleCtrl = GetComponent<PlayerScaleController>();
        _cachedPath = new NavMeshPath();

        _agent.speed = moveSpeed;
        _agent.angularSpeed = 0f;    // 회전 직접 처리
        _agent.acceleration = 50f;
        _agent.stoppingDistance = 0f;    // 목적지에서 멈추지 않음
        _agent.autoBraking = false;
        _agent.radius = baseAgentRadius;
        _agent.height = baseAgentHeight;
    }

    private void Start()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            _agent.enabled = false;
            return;
        }
        _prevScaleLevel = GetMyScaleLevel();
        StartCoroutine(InitAndRun());
    }

    // ── NavMesh 위에 스폰 확정 후 루프 시작 ──
    private IEnumerator InitAndRun()
    {
        _agent.enabled = false;

        // Agent Type 필터 초기화 (Awake에선 agentTypeID가 아직 0일 수 있어서 여기서)
        _navFilter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = NavMesh.AllAreas
        };

        // 이 Agent Type의 NavMesh 위 위치 확정
        float elapsed = 0f;
        while (elapsed < 5f)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 100f, _navFilter))
            {
                transform.position = hit.position;
                Debug.Log($"[AIBot] {name} NavMesh 위치 확정: {hit.position} agentTypeID={_agent.agentTypeID}");
                break;
            }
            elapsed += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        _agent.enabled = true;

        // isOnNavMesh 될 때까지 대기 (최대 3초)
        float timeout = 3f;
        while (!_agent.isOnNavMesh && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (!_agent.isOnNavMesh)
        {
            Debug.LogWarning($"[AIBot] {name} NavMesh 배치 실패, 재시도");
            yield break;
        }

        // isOnNavMesh 확정 후 Warp로 에이전트 내부 위치 동기화
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit warpHit, 5f, _navFilter))
            _agent.Warp(warpHit.position);

        _lastSetDestTime = -10f;
        EvaluateState();
        StartCoroutine(AILoop());
        StartCoroutine(WanderRoutine());
    }

    // ================================================================
    // FSM  (상태 평가 루프 — 이동 처리는 Update에서)
    // ================================================================

    private IEnumerator AILoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(pathRefreshRate);

            if (!_agent.enabled || !_agent.isOnNavMesh) continue;
            if (_currentState == AIState.ScaleIncreasing ||
                _currentState == AIState.ScaleDecreasing) continue;

            AIState prev = _currentState;
            EvaluateState();

            // 상태가 바뀌었을 때 Chase 타겟/타이머 초기화
            if (_currentState != prev)
            {
                _chaseTarget    = null;
                _chasePathTimer = 0f;
            }

            // Flee 로직
            if (_currentState == AIState.Flee)
            {
                Transform threat = FindThreat(GetMyScaleLevel());
                if (threat != null)
                {
                    Vector3 fleeDir = (transform.position - threat.position).normalized;
                    Vector3 fleeTarget = transform.position + fleeDir * 15f;

                    if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit fh, 10f, _navFilter))
                    {
                        // 도망갈 위치가 나와 너무 가깝다면 (벽에 막힌 경우) 방향을 살짝 꺾음
                        if (Vector3.Distance(transform.position, fh.position) < 3f)
                        {
                            Vector3 rightDir = Quaternion.Euler(0, 90, 0) * fleeDir;
                            fleeTarget = transform.position + rightDir * 10f;
                            if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit fh2, 10f, _navFilter))
                                _agent.SetDestination(fh2.position);
                        }
                        else
                        {
                            _agent.SetDestination(fh.position);
                        }
                    }
                }
            }
        }
    }

    private void EvaluateState()
    {
        // 스케일 변화 중이면 ScaleIncreasing / ScaleDecreasing 유지
        if (_scaleCtrl != null && _scaleCtrl.IsScaling)
        {
            if (_currentState != AIState.ScaleIncreasing && _currentState != AIState.ScaleDecreasing)
            {
                _stateBeforeScale = _currentState;
                int nowLevel = GetMyScaleLevel();
                _currentState = (nowLevel >= _prevScaleLevel)
                    ? AIState.ScaleIncreasing
                    : AIState.ScaleDecreasing;
                _prevScaleLevel = nowLevel;
            }
            return;
        }

        // 스케일링 끝났으면 이전 상태로 복귀
        if (_currentState == AIState.ScaleIncreasing || _currentState == AIState.ScaleDecreasing)
        {
            _prevScaleLevel = GetMyScaleLevel();
            _currentState = _stateBeforeScale;
        }

        int myLevel = GetMyScaleLevel();

        // 우선순위: Flee > Chase > Wander
        if (FindThreat(myLevel) != null) { _currentState = AIState.Flee; return; }
        if (FindNearestJelly() != null) { _currentState = AIState.Chase; return; }
        _currentState = AIState.Wander;
    }

    private bool TryGetWanderDestination(out Vector3 destination)
    {
        for (int i = 0; i < 15; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(5f, 20f);
            Vector3 candidate = transform.position
                + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 20f, _navFilter)
                || NavMesh.SamplePosition(candidate, out hit, 20f, NavMesh.AllAreas))
            {
                destination = hit.position;
                return true;
            }
        }

        // 최후 수단: 앞 방향 소폭 이동
        Vector3 fwd = transform.position + transform.forward * 5f;
        if (NavMesh.SamplePosition(fwd, out NavMeshHit fwHit, 10f, _navFilter)
            || NavMesh.SamplePosition(fwd, out fwHit, 10f, NavMesh.AllAreas))
        {
            destination = fwHit.position;
            return true;
        }

        destination = transform.position;
        return false;
    }

    // ── 매 프레임: 이동 + 회전 ──
    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!_agent.enabled || !_agent.isOnNavMesh) return;

        // ── 디버그 변수 갱신 ──
        _dbg_isOnNavMesh        = _agent.isOnNavMesh;
        _dbg_hasPath            = _agent.hasPath;
        _dbg_remainingDist      = _agent.remainingDistance;
        _dbg_distToWanderTarget = _hasWanderTarget ? Vector3.Distance(transform.position, _wanderTarget) : -1f;
        _dbg_hasWanderTarget    = _hasWanderTarget;
        _dbg_velocity           = _agent.velocity.magnitude;

        // Wander 정지 감지 (Chase 블록에서 별도 처리하므로 Wander 전용)
        if (_currentState == AIState.Wander)
        {
            if (_agent.velocity.magnitude < 0.05f)
            {
                _stopTimer += Time.deltaTime;
                if (_stopTimer >= STOP_LOG_THRESHOLD)
                {
                    Debug.LogWarning(
                        $"[AIBot:{name}] Wander 정지 {_stopTimer:F1}s | " +
                        $"isOnNavMesh={_dbg_isOnNavMesh} hasPath={_dbg_hasPath} " +
                        $"remainingDist={_dbg_remainingDist:F2} " +
                        $"hasWanderTarget={_dbg_hasWanderTarget} distToTarget={_dbg_distToWanderTarget:F2} " +
                        $"skipReason={_dbg_wanderSkipReason}");
                    _stopTimer = 0f;
                }
            }
            else { _stopTimer = 0f; }
        }

        // Chase: 0.15초 간격으로 CalculatePath + SetPath
        if (_currentState == AIState.Chase)
        {
            if (_chaseTarget == null)
                _chaseTarget = FindNearestJelly();

            _dbg_chaseTargetExists = (_chaseTarget != null);
            _dbg_distToChaseTarget = _chaseTarget != null
                ? Vector3.Distance(transform.position, _chaseTarget.position) : -1f;

            if (_chaseTarget != null)
            {
                _chasePathTimer += Time.deltaTime;
                if (_chasePathTimer >= CHASE_PATH_RATE)
                {
                    _chasePathTimer = 0f;
                    _cachedPath.ClearCorners();

                    // 젤리는 NavMesh 위에 없을 수 있으므로 가장 가까운 NavMesh 위치로 스냅
                    Vector3 chaseDestination = _chaseTarget.position;
                    if (NavMesh.SamplePosition(chaseDestination, out NavMeshHit chaseHit, 5f, NavMesh.AllAreas))
                        chaseDestination = chaseHit.position;

                    bool calcOk = _agent.CalculatePath(chaseDestination, _cachedPath);
                    _dbg_chasePathStatus = $"calcOk={calcOk} status={_cachedPath.status}";

                    if (calcOk && _cachedPath.status != NavMeshPathStatus.PathInvalid)
                    {
                        _agent.SetPath(_cachedPath);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[AIBot:{name}] Chase CalculatePath 실패 — " +
                            $"calcOk={calcOk} pathStatus={_cachedPath.status} " +
                            $"targetPos={_chaseTarget.position} myPos={transform.position} " +
                            $"distToTarget={_dbg_distToChaseTarget:F2} isOnNavMesh={_agent.isOnNavMesh}");
                    }
                }

                // Chase 중 0.5초 이상 정지 시 로그
                if (_agent.velocity.magnitude < 0.05f)
                {
                    _stopTimer += Time.deltaTime;
                    if (_stopTimer >= STOP_LOG_THRESHOLD)
                    {
                        Debug.LogWarning(
                            $"[AIBot:{name}] Chase 정지 {_stopTimer:F1}s | " +
                            $"hasPath={_dbg_hasPath} remainingDist={_dbg_remainingDist:F2} " +
                            $"targetExists={_dbg_chaseTargetExists} distToTarget={_dbg_distToChaseTarget:F2} " +
                            $"chasePathStatus={_dbg_chasePathStatus}");
                        _stopTimer = 0f;
                    }
                }
                else { _stopTimer = 0f; }
            }
            else
            {
                _chasePathTimer       = 0f;
                _dbg_chasePathStatus  = "no target";
                EvaluateState();
            }
        }
        else
        {
            _chasePathTimer      = 0f;
            _dbg_chasePathStatus = "-";
        }

        // 진행 방향으로 스무스 회전
        Vector3 vel = _agent.velocity;
        if (vel.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(vel.normalized),
                rotateSpeed * Time.deltaTime);
        }

        // 애니메이터
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
            anim.SetBool("IsMoving", vel.magnitude > 0.1f);
    }

    // ── Wander: 동기 CalculatePath + SetPath로 pathPending 문제 완전 회피 ──
    private IEnumerator WanderRoutine()
    {
        while (true)
        {
            yield return null;

            if (!_agent.enabled || !_agent.isOnNavMesh)
            {
                _dbg_wanderSkipReason = $"agent not ready (enabled={_agent.enabled} onMesh={_agent.isOnNavMesh})";
                continue;
            }

            // Wander 상태가 아니면 플래그 초기화 (재진입 시 즉시 새 목적지 설정)
            if (_currentState != AIState.Wander)
            {
                _hasWanderTarget      = false;
                _dbg_wanderSkipReason = "not Wander state";
                continue;
            }

            float distToTarget = _hasWanderTarget
                ? Vector3.Distance(transform.position, _wanderTarget) : -1f;

            // SetPath 이후 0.5초 쿨다운 내에는 hasPath=False여도 실패로 보지 않음
            bool pathFailed = _hasWanderTarget && !_agent.hasPath
                              && (Time.time - _lastSetDestTime) > 0.5f;
            bool needNew = !_hasWanderTarget || distToTarget < 1.5f || pathFailed;

            if (!needNew)
            {
                _dbg_wanderSkipReason = $"moving (dist={distToTarget:F2})";
                continue;
            }

            if (TryGetWanderDestination(out Vector3 dest))
            {
                // 동기 경로 계산 → SetPath로 즉시 적용 (pathPending=True 발생 없음)
                _cachedPath.ClearCorners();
                if (_agent.CalculatePath(dest, _cachedPath)
                    && _cachedPath.status == NavMeshPathStatus.PathComplete)
                {
                    _wanderTarget    = dest;
                    _hasWanderTarget = true;
                    _agent.SetPath(_cachedPath);
                    _lastSetDestTime = Time.time;
                    _dbg_wanderSkipReason = $"set path dist={Vector3.Distance(transform.position, dest):F2}";
                }
                else
                {
                    // 경로 부분적이거나 없음 → Partial이면 일단 허용, Invalid면 재시도
                    if (_cachedPath.status == NavMeshPathStatus.PathPartial
                        && _cachedPath.corners.Length > 1)
                    {
                        _wanderTarget    = _cachedPath.corners[^1];
                        _hasWanderTarget = true;
                        _agent.SetPath(_cachedPath);
                        _lastSetDestTime = Time.time;
                        _dbg_wanderSkipReason = $"set partial path corners={_cachedPath.corners.Length}";
                    }
                    else
                    {
                        _hasWanderTarget      = false;
                        _dbg_wanderSkipReason = $"CalculatePath failed status={_cachedPath.status} dest={dest}";
                        Debug.LogWarning($"[AIBot:{name}] CalculatePath 실패 status={_cachedPath.status} dest={dest}");
                    }
                }
            }
            else
            {
                _dbg_wanderSkipReason  = "TryGetWanderDestination FAILED";
                _hasWanderTarget       = false;
                Debug.LogWarning($"[AIBot:{name}] TryGetWanderDestination 실패 — NavMesh 도달 가능 지점 없음");
            }
        }
    }

    // ================================================================
    // 외부 콜백
    // ================================================================

    /// <summary>
    /// 봇 스케일 증가 완료 시 PlayerScaleController가 호출.
    /// NavMeshAgent.Warp()로 지면 관통 없이 재배치 후 즉시 경로 재개.
    /// </summary>
    public void RecenterCC()   // 이름은 PlayerScaleController 호환 유지
    {
        StartCoroutine(OnScaleChanged());
    }

    private IEnumerator OnScaleChanged()
    {
        if (!_agent.enabled)
        {
            // 에이전트가 꺼진 상태면 켠 뒤 처리
            yield return null;
            _agent.enabled = true;
            yield return null;
        }

        // 현재 스케일에 맞게 에이전트 캡슐 크기 갱신
        float s = transform.localScale.x;
        _agent.radius = baseAgentRadius * s;
        _agent.height = baseAgentHeight * s;

        // NavMesh 위로 Warp (지면 박힘 해소)
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f * s, _navFilter))
            _agent.Warp(hit.position);

        // 상태 복귀
        _prevScaleLevel = GetMyScaleLevel();
        _currentState = _stateBeforeScale;
        _chaseTarget = null;
    }

    /// <summary>봇 스케일 감소 시작 시 외부에서 호출 (ex. 우유 맞음)</summary>
    public void OnBotScaleDecreaseStart()
    {
        if (_currentState != AIState.ScaleIncreasing && _currentState != AIState.ScaleDecreasing)
            _stateBeforeScale = _currentState;
        _currentState = AIState.ScaleDecreasing;
    }

    // ================================================================
    // 탐지 함수
    // ================================================================

    private int GetMyScaleLevel()
        => _scaleCtrl != null ? _scaleCtrl.BotScaleLevel : 1;

    private Transform FindThreat(int myLevel)
    {
        Collider[] cols = Physics.OverlapSphere(transform.position, detectRadius);
        foreach (var col in cols)
        {
            NetworkPlayerSync p = col.GetComponentInParent<NetworkPlayerSync>();
            if (p != null && p.ScaleLevel > myLevel + 1) return p.transform;

            AIPlayerMovement b = col.GetComponentInParent<AIPlayerMovement>();
            if (b != null && b != this && b.transform.localScale.x > transform.localScale.x + 0.3f)
                return b.transform;
        }
        return null;
    }

    private Transform FindNearestJelly()
    {
        JellyObject[] all = FindObjectsByType<JellyObject>(FindObjectsSortMode.None);
        Transform nearest = null;
        float minDist = detectRadius;
        foreach (var j in all)
        {
            if (j == null) continue;
            float d = Vector3.Distance(transform.position, j.transform.position);
            if (d < minDist) { minDist = d; nearest = j.transform; }
        }
        return nearest;
    }

    // ================================================================
    // 디버그
    // ================================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, _currentState.ToString());
#endif
    }
}