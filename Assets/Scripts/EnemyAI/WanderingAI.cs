using UnityEngine;
using UnityEngine.AI; // NavMeshAgent ����� ���� �ʼ�
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class WanderingAI : MonoBehaviour
{
    [Header("Wandering Settings")]
    [Tooltip("�̵��� �ݰ� (���� ��ġ ���� Ȥ�� �ʱ� ��ġ ����)")]
    public float wanderRadius = 10f;

    [Tooltip("�̵� �� ��� �ð� (�ּ�)")]
    public float minWaitTime = 1f;

    [Tooltip("�̵� �� ��� �ð� (�ִ�)")]
    public float maxWaitTime = 3f;

    [Tooltip("true�� ó�� ������ ��ġ�� �߽����� ��ȸ, false�� ���� ��ġ�� �߽����� ��� �̵�")]
    public bool anchorToInitialPosition = false;

    private NavMeshAgent agent;
    private Vector3 initialPosition; // ������ �����
    private bool isWaiting = false;

    public Animator jellyAnimController;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        // [�߰���] �켱������ �����ϰ� �����Ͽ� ���� ���� �������� �������� (0~99)
        // ���ڰ� �������� �켱������ ���� (���� ������)
        agent.avoidancePriority = Random.Range(0, 100);

        // ���� ��ġ ����
        initialPosition = transform.position;

        // ù �̵� ����
        MoveToRandomPosition();
    }

    void Update()
    {
        // ��� ���̶�� �ƹ��͵� �� ��
        if (isWaiting) return;

        if (agent == null) { return; }

        // �������� ���� �����ߴ��� Ȯ��
        // pathPending: ��� ��� ������ Ȯ�� (��� ���� �� �����ߴٰ� �Ǵ��ϴ� ���� ����)
        if (!agent.isOnNavMesh) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
            {
                StartCoroutine(WaitAndMove());
            }
        }
    }

    /// <summary>
    /// ���� �� ���� �ð� ����ߴٰ� ���� ������ ����
    /// </summary>
    IEnumerator WaitAndMove()
    {
        isWaiting = true;
        jellyAnimController.SetBool("IsMoving", !isWaiting);

        // ������ �ð���ŭ ���
        float waitTime = Random.Range(minWaitTime, maxWaitTime);
        yield return new WaitForSeconds(waitTime);

        MoveToRandomPosition();
        isWaiting = false;
        jellyAnimController.SetBool("IsMoving", !isWaiting);
    }

    /// <summary>
    /// ������ ��ġ�� ����ϰ� Agent���� �̵� ����
    /// </summary>
    void MoveToRandomPosition()
    {
        // NavMesh 위에 없으면 이동 명령 스킵
        if (!agent.isOnNavMesh) return;

        Vector3 origin = anchorToInitialPosition ? initialPosition : transform.position;
        Vector3 newPos = GetRandomPointOnNavMesh(origin, wanderRadius);
        agent.SetDestination(newPos);
    }

    /// <summary>
    /// NavMesh.SamplePosition�� �̿��� ��ȿ�� ������ ��ǥ ��ȯ
    /// </summary>
    public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float range)
    {
        for (int i = 0; i < 30; i++) // 30�� ���� �õ� (�� ã�� ���� �����Ƿ�)
        {
            // 1. ��ü ������ ���� ��ǥ ����
            Vector3 randomPoint = center + Random.insideUnitSphere * range;

            // 2. �ش� ��ǥ ��ó�� NavMesh(�̵� ���� ����)�� �ִ��� Ȯ��
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 1.0f, NavMesh.AllAreas))
            {
                // 3. ã�Ҵٸ� �� ��ġ ��ȯ
                return hit.position;
            }
        }

        // �� ã������ �׳� ���� ��ȯ (���� ����)
        return center;
    }

    // ������ �󿡼� �̵� �ݰ��� ������ ���� ���� �����
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = Application.isPlaying && anchorToInitialPosition ? initialPosition : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}