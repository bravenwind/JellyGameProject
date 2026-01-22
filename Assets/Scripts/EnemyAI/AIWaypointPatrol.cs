using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class AIWaypointPatrol : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("AI가 순찰할 웨이포인트 지점들입니다.")]
    public Transform[] waypoints;

    [Tooltip("각 지점에 도착 후 대기할 시간(초)입니다.")]
    public float waitTime = 1.0f;

    private NavMeshAgent agent;
    private Animator animator;
    private int currentWaypointIndex = 0;
    private bool isWaiting = false;

    // [추가] 정방향(1->4)인지 역방향(4->1)인지 체크하는 변수
    private bool movingForward = true;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (waypoints.Length == 0)
        {
            Debug.LogError("웨이포인트가 설정되지 않았습니다! Inspector에서 할당해주세요.");
            return;
        }

        MoveToNextWaypoint();
    }

    void Update()
    {
        if (waypoints.Length == 0 || isWaiting) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            StartCoroutine(WaitAndMove());
        }
    }

    void MoveToNextWaypoint()
    {
        // 1. 현재 인덱스로 이동 설정
        agent.destination = waypoints[currentWaypointIndex].position;

        // 애니메이션 켜기
        if (animator != null)
        {
            animator.SetBool("IsMoving", true);
        }

        // 2. [수정됨] 다음 인덱스 계산 (왕복 로직)
        if (movingForward)
        {
            // 정방향 이동 중이라면 인덱스 증가
            if (currentWaypointIndex >= waypoints.Length - 1)
            {
                // 마지막 지점에 도달했다면 방향을 뒤집고 인덱스 감소
                movingForward = false;
                currentWaypointIndex--;
            }
            else
            {
                currentWaypointIndex++;
            }
        }
        else
        {
            // 역방향 이동 중이라면 인덱스 감소
            if (currentWaypointIndex <= 0)
            {
                // 시작 지점에 도달했다면 방향을 다시 정방향으로 하고 인덱스 증가
                movingForward = true;
                currentWaypointIndex++;
            }
            else
            {
                currentWaypointIndex--;
            }
        }
    }

    IEnumerator WaitAndMove()
    {
        isWaiting = true;

        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        if (animator != null)
        {
            animator.SetBool("IsMoving", false);
        }

        yield return new WaitForSeconds(waitTime);

        agent.isStopped = false;
        isWaiting = false;

        MoveToNextWaypoint();
    }
}