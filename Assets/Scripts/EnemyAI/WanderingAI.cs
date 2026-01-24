using UnityEngine;
using UnityEngine.AI; // NavMeshAgent 사용을 위해 필수
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
    private Vector3 initialPosition; // 기준점 저장용
    private bool isWaiting = false;

    public Animator jellyAnimController;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        // [추가됨] 우선순위를 랜덤하게 설정하여 누가 먼저 지나갈지 결정해줌 (0~99)
        // 숫자가 낮을수록 우선순위가 높음 (먼저 지나감)
        agent.avoidancePriority = Random.Range(0, 100);

        // 시작 위치 저장
        initialPosition = transform.position;

        // 첫 이동 시작
        MoveToRandomPosition();
    }

    void Update()
    {
        // 대기 중이라면 아무것도 안 함
        if (isWaiting) return;

        if (agent == null) { return; }

        // 목적지에 거의 도착했는지 확인
        // pathPending: 경로 계산 중인지 확인 (계산 중일 때 도착했다고 판단하는 오류 방지)
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
            {
                // 도착 완료 -> 대기 코루틴 시작
                StartCoroutine(WaitAndMove());
            }
        }
    }

    /// <summary>
    /// 도착 후 일정 시간 대기했다가 다음 목적지 설정
    /// </summary>
    IEnumerator WaitAndMove()
    {
        isWaiting = true;
        jellyAnimController.SetBool("IsMoving", !isWaiting);

        // 랜덤한 시간만큼 대기
        float waitTime = Random.Range(minWaitTime, maxWaitTime);
        yield return new WaitForSeconds(waitTime);

        MoveToRandomPosition();
        isWaiting = false;
        jellyAnimController.SetBool("IsMoving", !isWaiting);
    }

    /// <summary>
    /// 무작위 위치를 계산하고 Agent에게 이동 명령
    /// </summary>
    void MoveToRandomPosition()
    {
        // 기준점 설정 (초기 위치 고정 or 현재 위치 기준)
        Vector3 origin = anchorToInitialPosition ? initialPosition : transform.position;

        // NavMesh 위의 유효한 랜덤 좌표 구하기
        Vector3 newPos = GetRandomPointOnNavMesh(origin, wanderRadius);

        agent.SetDestination(newPos);
    }

    /// <summary>
    /// NavMesh.SamplePosition을 이용해 유효한 무작위 좌표 반환
    /// </summary>
    public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float range)
    {
        for (int i = 0; i < 30; i++) // 30번 정도 시도 (못 찾을 수도 있으므로)
        {
            // 1. 구체 형태의 랜덤 좌표 생성
            Vector3 randomPoint = center + Random.insideUnitSphere * range;

            // 2. 해당 좌표 근처에 NavMesh(이동 가능 구역)가 있는지 확인
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 1.0f, NavMesh.AllAreas))
            {
                // 3. 찾았다면 그 위치 반환
                return hit.position;
            }
        }

        // 못 찾았으면 그냥 센터 반환 (에러 방지)
        return center;
    }

    // 에디터 상에서 이동 반경을 눈으로 보기 위한 기즈모
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = Application.isPlaying && anchorToInitialPosition ? initialPosition : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}