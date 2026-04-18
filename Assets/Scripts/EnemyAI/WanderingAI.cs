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
        // 이제 여기서 굳이 애니메이션을 안 켜줘도 Update에서 자동으로 켜줍니다!
    }

    void Update()
    {
        // 💡 [핵심 해결책] NavMeshAgent의 실제 물리적 이동 속도를 기반으로 애니메이션 동기화
        if (jellyAnimController != null && agent.isOnNavMesh)
        {
            // agent.velocity.magnitude는 실제 이동하는 속도입니다.
            // 속도가 0.1보다 크면(이동 중이면) 걷기, 아니면 대기 애니메이션 재생
            bool isActuallyMoving = agent.velocity.magnitude > 0.1f;
            jellyAnimController.SetBool("IsMoving", isActuallyMoving);
        }

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
        // SetMovingAnim(false); <-- 삭제! (Update에서 알아서 꺼줌)

        float waitTime = Random.Range(minWaitTime, maxWaitTime);
        yield return new WaitForSeconds(waitTime);

        MoveToRandomPosition();
        isWaiting = false;
        // SetMovingAnim(true); <-- 삭제! (Update에서 알아서 켜줌)
    }

    void MoveToRandomPosition()
    {
        if (!agent.isOnNavMesh) return;

        Vector3 origin = anchorToInitialPosition ? initialPosition : transform.position;

        if (TryGetRandomPointOnNavMesh(origin, wanderRadius, out Vector3 newPos))
        {
            agent.SetDestination(newPos);
        }
    }

    public static bool TryGetRandomPointOnNavMesh(Vector3 center, float range, out Vector3 result)
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 circle = Random.insideUnitCircle * range;
            Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

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

    public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float range)
    {
        TryGetRandomPointOnNavMesh(center, range, out Vector3 result);
        return result;
    }

    // SetMovingAnim() 함수는 이제 필요 없으므로 완전히 삭제했습니다.

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = Application.isPlaying && anchorToInitialPosition ? initialPosition : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}