using UnityEngine;
using UnityEngine.AI; // NavMeshAgent를 사용하기 위해 필수입니다.
using System.Collections;

public class AIWaypointPatrol : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("AI가 순찰할 웨이포인트 지점들입니다.")]
    public Transform[] waypoints; // 이동할 지점 배열

    [Tooltip("각 지점에 도착 후 대기할 시간(초)입니다.")]
    public float waitTime = 1.0f;

    private NavMeshAgent agent;
    private int currentWaypointIndex = 0; // 현재 목표 지점의 인덱스
    private bool isWaiting = false; // 대기 중인지 확인하는 플래그

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        // 방어 코드: 웨이포인트가 없으면 실행 중지
        if (waypoints.Length == 0)
        {
            Debug.LogError("웨이포인트가 설정되지 않았습니다! Inspector에서 할당해주세요.");
            return;
        }

        // 첫 번째 지점으로 이동 시작
        MoveToNextWaypoint();
    }

    void Update()
    {
        // 웨이포인트가 없거나 대기 중이라면 로직을 수행하지 않음
        if (waypoints.Length == 0 || isWaiting) return;

        // 목적지에 도착했는지 확인
        // pathPending: 경로 계산 중인지 확인 (계산 중일 때 도착으로 착각하는 것 방지)
        // remainingDistance: 남은 거리
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            StartCoroutine(WaitAndMove());
        }
    }

    // 다음 웨이포인트로 이동 명령을 내리는 함수
    void MoveToNextWaypoint()
    {
        // NavMeshAgent에게 목적지 설정
        agent.destination = waypoints[currentWaypointIndex].position;

        // 다음 인덱스로 변경 (배열 길이를 넘어가면 0으로 돌아감 -> 순환 구조)
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
    }

    // 도착 후 잠시 대기했다가 이동하는 코루틴
    IEnumerator WaitAndMove()
    {
        isWaiting = true;

        // [추가됨] 도착했으면 엔진을 끕니다. (미세 조정 방지)
        agent.isStopped = true;
        agent.velocity = Vector3.zero; // 관성 제거

        // 설정된 시간만큼 대기
        yield return new WaitForSeconds(waitTime);

        // [추가됨] 이동 재개 시 엔진을 다시 켭니다.
        agent.isStopped = false;

        isWaiting = false;
        MoveToNextWaypoint();
    }
}