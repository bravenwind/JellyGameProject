// ============================================================
// AIPlayerMovement.cs
// ============================================================
// 역할: NavMeshAgent 기반 AI 이동 컨트롤러 (FSM 패턴)
//
// PlayerController와 동일한 FSM 구조:
//   - AIBaseState 추상 클래스 상속
//   - 상태별 스크립트 분리 (Assets/Scripts/AI/FSM/)
//   - ChangeState()로 상태 전환, Enter/Update/Exit 생명주기
//
// FSM 상태:
//   Wander  — 랜덤 배회
//   Chase   — 젤리 추적
//   Flee    — 위협(나보다 큰 상대) 도망
//   Scale   — 크기 변화 중 (이동 일시정지)
//
// 흡수 처리:
//   - OnTriggerEnter → 나보다 작은 플레이어를 만나면 흡수
//   - RPC_BotAbsorbed → 나보다 큰 플레이어에게 먹힘
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PhotonView))]
public class AIPlayerMovement : MonoBehaviourPun
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

    // ─────────────────────────────────────────────────────────
    // 컴포넌트 (상태 클래스들이 접근)
    // ─────────────────────────────────────────────────────────
    public NavMeshAgent Agent { get; private set; }
    public PlayerScaleController ScaleCtrl { get; private set; }
    public NavMeshQueryFilter NavFilter { get; private set; }
    public NavMeshPath CachedPath { get; private set; }

    private Animator _anim;

    // ─────────────────────────────────────────────────────────
    // FSM
    // ─────────────────────────────────────────────────────────
    [Header("FSM (읽기 전용)")]
    [SerializeField] private string _dbg_currentState = "-";

    private AIBaseState _currentState;
    public  AIBaseState CurrentState => _currentState;

    // 상태 인스턴스 (Start에서 1회 생성, 재사용)
    public AIWanderState WanderState { get; private set; }
    public AIChaseState  ChaseState  { get; private set; }
    public AIFleeState   FleeState   { get; private set; }
    public AIScaleState  ScaleState  { get; private set; }

    // Scale 상태 복귀용
    private AIBaseState _stateBeforeScale;
    private float _prevScaleValue = 1f;
    public bool IsBeingAbsorbed { get; set; } = false;

    // ─────────────────────────────────────────────────────────
    // 외부 프로퍼티
    // ─────────────────────────────────────────────────────────

    /// <summary>이 봇의 현재 스케일 값</summary>
    public float CurrentScale => transform.localScale.x;

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        Agent     = GetComponent<NavMeshAgent>();
        ScaleCtrl = GetComponent<PlayerScaleController>();
        CachedPath = new NavMeshPath();
        _anim     = GetComponentInChildren<Animator>();

        Agent.speed           = moveSpeed;
        Agent.angularSpeed    = 0f;    // 회전 직접 처리
        Agent.acceleration    = 50f;
        Agent.stoppingDistance = 0f;
        Agent.autoBraking     = false;
        Agent.radius          = baseAgentRadius;
        Agent.height          = baseAgentHeight;
    }

    private void Start()
    {
        // 상태 인스턴스 생성 (PlayerController 패턴과 동일)
        WanderState = new AIWanderState(this);
        ChaseState  = new AIChaseState(this);
        FleeState   = new AIFleeState(this);
        ScaleState  = new AIScaleState(this);

        if (!PhotonNetwork.IsMasterClient)
        {
            Agent.enabled = false;
            return;
        }

        _prevScaleValue = transform.localScale.x;
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

        // NavMesh 위 위치 확정
        float elapsed = 0f;
        while (elapsed < 5f)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 100f, NavFilter))
            {
                transform.position = hit.position;
                break;
            }
            elapsed += 0.2f;
            yield return new WaitForSeconds(0.2f);
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

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit warpHit, 5f, NavFilter))
            Agent.Warp(warpHit.position);

        Agent.avoidancePriority = Random.Range(20, 80);

        // 첫 상태 진입
        ChangeState(WanderState);
        StartCoroutine(StateEvalLoop());
    }

    // ─────────────────────────────────────────────────────────
    // 상태 관리 (PlayerController.ChangeState 패턴)
    // ─────────────────────────────────────────────────────────

    public void ChangeState(AIBaseState newState)
    {
        if (_currentState == newState) return;

        _currentState?.Exit();
        _currentState = newState;
        _currentState?.Enter();

        _dbg_currentState = _currentState?.GetType().Name ?? "-";
    }

    /// <summary>주기적으로 상태 전환 평가 (우선순위: Scale > Flee > Chase > Wander)</summary>
    private IEnumerator StateEvalLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(stateEvalRate);

            if (!Agent.enabled || !Agent.isOnNavMesh) continue;

            // Scale 상태 중이면 ScaleState.Update()가 복귀를 처리
            if (_currentState == ScaleState) continue;

            EvaluateAndTransition();
        }
    }

    /// <summary>현재 상황을 평가하여 적절한 상태로 전환</summary>
    public void EvaluateAndTransition()
    {
        // 스케일 변화 중이면 ScaleState 진입
        if (ScaleCtrl != null && ScaleCtrl.IsScaling)
        {
            if (_currentState != ScaleState)
            {
                _stateBeforeScale = _currentState;
                float nowScale = transform.localScale.x;
                ScaleState.IsIncreasing = (nowScale >= _prevScaleValue);
                _prevScaleValue = nowScale;
                ChangeState(ScaleState);
            }
            return;
        }

        // 우선순위: Flee > Chase > Wander
        if (FindThreat() != null) { ChangeState(FleeState); return; }
        if (FindTargetToChase() != null) { ChangeState(ChaseState); return; }
        ChangeState(WanderState);
    }

    /// <summary>ScaleState 완료 후 이전 상태로 복귀 (AIScaleState에서 호출)</summary>
    public void RestoreStateBeforeScale()
    {
        _prevScaleValue = transform.localScale.x;
        ChangeState(_stateBeforeScale ?? WanderState);
    }

    // ─────────────────────────────────────────────────────────
    // Update: 현재 상태 업데이트 + 회전 + 애니메이션
    // ─────────────────────────────────────────────────────────
    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!Agent.enabled || !Agent.isOnNavMesh) return;

        // 현재 상태 Update
        _currentState?.Update();

        // 진행 방향으로 스무스 회전
        Vector3 vel = Agent.velocity;
        if (vel.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(vel.normalized),
                rotateSpeed * Time.deltaTime);
        }

        // 애니메이터
        if (_anim != null)
            _anim.SetBool("IsMoving", vel.magnitude > 0.1f);
    }

    // ─────────────────────────────────────────────────────────
    // 탐지 함수 (상태 클래스들이 호출)
    // ─────────────────────────────────────────────────────────

    public float GetMyScale()
        => transform.localScale.x;

    /// <summary>
    /// 나보다 큰 위협을 탐지.
    /// 스케일(덩치)을 고려하여 중심점이 아닌 '표면 간의 거리(Edge Distance)'를 기준으로 탐지합니다.
    /// </summary>
    public Transform FindThreat()
    {
        float myScale = transform.localScale.x;
        Transform closest = null;

        // 탐지 거리 안쪽인지 확인할 때는 closestDist 대신 최소 표면 거리를 기록
        float minEdgeDist = float.MaxValue;

        // 플레이어 위협
        foreach (var p in FindObjectsByType<NetworkPlayerSync>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            if (p.GetComponentInParent<AIPlayerMovement>() == this) continue;

            float threatScale = p.transform.localScale.x;
            if (threatScale <= myScale) continue; // 나보다 작거나 같으면 위협 아님

            // 중심점 간의 거리
            float distToCenter = Vector3.Distance(transform.position, p.transform.position);

            // 표면 간의 거리 = 중심거리 - 내 반지름 - 상대 반지름
            // (baseAgentRadius가 0.5라면, scale * 0.5가 실제 콜라이더의 반지름이 됩니다)
            float edgeDist = distToCenter - (myScale * baseAgentRadius) - (threatScale * baseAgentRadius);

            // 실제 표면 거리가 탐지 반경(detectRadius) 이내에 들어왔는가?
            if (edgeDist < detectRadius && edgeDist < minEdgeDist)
            {
                minEdgeDist = edgeDist;
                closest = p.transform;
            }
        }

        // 다른 AIPlayerMovement 봇 위협
        foreach (var b in FindObjectsByType<AIPlayerMovement>(FindObjectsSortMode.None))
        {
            if (b == null || b == this) continue;

            float threatScale = b.transform.localScale.x;
            if (threatScale <= myScale) continue;

            float distToCenter = Vector3.Distance(transform.position, b.transform.position);
            float edgeDist = distToCenter - (myScale * baseAgentRadius) - (threatScale * baseAgentRadius);

            if (edgeDist < detectRadius && edgeDist < minEdgeDist)
            {
                minEdgeDist = edgeDist;
                closest = b.transform;
            }
        }

        return closest;
    }

    /// <summary>
    /// 나보다 작은 먹잇감(플레이어 또는 다른 봇)을 탐지합니다.
    /// </summary>
    public Transform FindPrey()
    {
        float myScale = transform.localScale.x;
        Transform closest = null;
        float minEdgeDist = float.MaxValue;

        // 1. 작은 플레이어 탐색
        foreach (var p in FindObjectsByType<NetworkPlayerSync>(FindObjectsSortMode.None))
        {
            if (p == null || p.GetComponentInParent<AIPlayerMovement>() == this) continue;

            float preyScale = p.transform.localScale.x;
            if (preyScale >= myScale) continue; // 나보다 크거나 같으면 사냥감이 아님!

            float distToCenter = Vector3.Distance(transform.position, p.transform.position);
            float edgeDist = distToCenter - (myScale * baseAgentRadius) - (preyScale * baseAgentRadius);

            if (edgeDist < detectRadius && edgeDist < minEdgeDist)
            {
                minEdgeDist = edgeDist;
                closest = p.transform;
            }
        }

        // 2. 작은 다른 AI 봇 탐색
        foreach (var b in FindObjectsByType<AIPlayerMovement>(FindObjectsSortMode.None))
        {
            if (b == null || b == this) continue;

            float preyScale = b.transform.localScale.x;
            if (preyScale >= myScale) continue;

            float distToCenter = Vector3.Distance(transform.position, b.transform.position);
            float edgeDist = distToCenter - (myScale * baseAgentRadius) - (preyScale * baseAgentRadius);

            if (edgeDist < detectRadius && edgeDist < minEdgeDist)
            {
                minEdgeDist = edgeDist;
                closest = b.transform;
            }
        }

        if (closest != null) 
        {
            Debug.Log("AIPlayerMovement/FindPrey : " + closest.name);
        }

        return closest;
    }

    /// <summary>
    /// 추적할 대상을 결정합니다. (작은 플레이어를 1순위, 젤리를 2순위로 탐색)
    /// </summary>
    public Transform FindTargetToChase()
    {
        Transform prey = FindPrey();
        if (prey != null) return prey; // 근처에 맛있는 플레이어가 있으면 플레이어를 먼저 쫓아감!

        return FindNearestJelly(); // 없으면 그냥 바닥에 있는 젤리 찾음
    }

    /// <summary>탐지 반경 내 가장 가까운 젤리</summary>
    public Transform FindNearestJelly()
    {
        JellyObject[] all = FindObjectsByType<JellyObject>(FindObjectsSortMode.None);
        Transform nearest = null;
        float minDist = detectRadius;

        foreach (var j in all)
        {
            if (j == null) continue;
            float d = Vector3.Distance(transform.position, j.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = j.transform;
            }
        }
        return nearest;
    }

    /// <summary>배회용 랜덤 NavMesh 위치 탐색</summary>
    public bool TryGetWanderDestination(out Vector3 destination)
    {
        for (int i = 0; i < 15; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(5f, 20f);
            Vector3 candidate = transform.position
                + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 20f, NavFilter)
                || NavMesh.SamplePosition(candidate, out hit, 20f, NavMesh.AllAreas))
            {
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

        // NavMesh 위로 Warp
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f * s, NavFilter))
            Agent.Warp(hit.position);

        // ScaleState 복귀는 AIScaleState.Update()가 IsScaling=false를 감지해 처리함.
        // 여기서 RestoreStateBeforeScale()을 중복 호출하면 EvaluateAndTransition()이
        // 이미 FleeState로 전환한 것을 WanderState로 되돌리는 경쟁 조건이 발생함.
        if (_currentState == ScaleState)
            RestoreStateBeforeScale();
    }

    /// <summary>봇 스케일 감소 시작 시 외부 호출</summary>
    public void OnBotScaleDecreaseStart()
    {
        if (_currentState != ScaleState)
        {
            _stateBeforeScale = _currentState;
            ScaleState.IsIncreasing = false;
            ChangeState(ScaleState);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 흡수 처리 (플레이어 ↔ AI)
    // ─────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // ── 더 작은 플레이어 흡수 ──
        NetworkPlayerSync player = other.GetComponentInParent<NetworkPlayerSync>();
        if (player != null && player.transform.localScale.x < transform.localScale.x)
        {
            float preyScale = player.transform.localScale.x;
            player.photonView.RPC("RPC_GetAbsorbed", RpcTarget.All, photonView.ViewID);
            ScaleCtrl?.GrowByAbsorbing(preyScale);
            return;
        }

        // ── 더 작은 AI 봇 흡수 ──
        AIPlayerMovement otherBot = other.GetComponentInParent<AIPlayerMovement>();
        if (otherBot != null && otherBot != this
            && !otherBot.IsBeingAbsorbed
            && otherBot.transform.localScale.x < transform.localScale.x)
        {
            float preyScale = otherBot.transform.localScale.x;
            otherBot.photonView.RPC(nameof(RPC_BotAbsorbed), RpcTarget.All, photonView.ViewID);
            ScaleCtrl?.GrowByAbsorbing(preyScale);
            return;
        }
    }

    /// <summary>[RPC] 이 봇이 흡수당했을 때 (모든 클라이언트에서 실행)</summary>
    [PunRPC]
    private void RPC_BotAbsorbed(int absorberViewID)
    {
        if (IsBeingAbsorbed) return;
        IsBeingAbsorbed = true;
        StartCoroutine(BotAbsorbedSequence(absorberViewID));
    }

    private IEnumerator BotAbsorbedSequence(int absorberViewID)
    {
        if (Agent != null) Agent.enabled = false;
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
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (PhotonNetwork.IsMasterClient)
            PhotonNetwork.Destroy(gameObject);
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
