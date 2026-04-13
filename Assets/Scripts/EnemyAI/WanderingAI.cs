using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class WanderingAI : MonoBehaviour
{
    [Header("Wandering Settings")]
    [Tooltip("이동할 반경 (현재 위치 기준 혹은 초기 위치 기준)")]
    public float wanderRadius = 10f;

    [Tooltip("이동 후 대기 시간 (최소)")]
    public float minWaitTime = 1f;

    [Tooltip("이동 후 대기 시간 (최대)")]
    public float maxWaitTime = 3f;

    [Tooltip("true면 처음 스폰된 위치를 중심으로 배회, false면 현재 위치를 중심으로 계속 이동")]
    public bool anchorToInitialPosition = false;

    private NavMeshAgent agent;
    private Vector3 initialPosition;
    private bool isWaiting = false;

    public Animator jellyAnimController;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        agent.avoidancePriority = Random.Range(0, 100);
        initialPosition = transform.position;
        MoveToRandomPosition();
    }

    void Update()
    {
        if (isWaiting) return;
        if (!agent.isOnNavMesh) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
            {
                StartCoroutine(WaitAndMove());
            }
        }
    }

    IEnumerator WaitAndMove()
    {
        isWaiting = true;
        SetMovingAnim(false);

        float waitTime = Random.Range(minWaitTime, maxWaitTime);
        yield return new WaitForSeconds(waitTime);

        MoveToRandomPosition();
        isWaiting = false;
        SetMovingAnim(true);
    }

    void MoveToRandomPosition()
    {
        if (!agent.isOnNavMesh) return;

        Vector3 origin = anchorToInitialPosition ? initialPosition : transform.position;

        if (TryGetRandomPointOnNavMesh(origin, wanderRadius, out Vector3 newPos))
        {
            agent.SetDestination(newPos);
        }
        // 찾기 실패 시 SetDestination 호출 안 함 → 다음 WaitAndMove에서 재시도
    }

    // 반환값으로 성공/실패 명확히 구분
    public static bool TryGetRandomPointOnNavMesh(Vector3 center, float range, out Vector3 result)
    {
        for (int i = 0; i < 30; i++)
        {
            // insideUnitCircle: XZ 평면 2D 원 안에서 뽑아 y 튀는 문제 방지
            Vector2 circle = Random.insideUnitCircle * range;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

            // SamplePosition 탐색 반경을 range 비례로 설정 (고정 1.0f → 동적)
            float sampleRadius = Mathf.Max(2f, range * 0.3f);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, sampleRadius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = center;
        return false;
    }

    // 하위 호환용 static 메서드 (기존 코드에서 호출하는 곳 있으면 그대로 동작)
    public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float range)
    {
        TryGetRandomPointOnNavMesh(center, range, out Vector3 result);
        return result;
    }

    private void SetMovingAnim(bool moving)
    {
        if (jellyAnimController != null)
            jellyAnimController.SetBool("IsMoving", moving);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = Application.isPlaying && anchorToInitialPosition ? initialPosition : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}
