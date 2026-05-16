using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class AIWaypointPatrol : MonoBehaviour
{
    [Header("����")]
    [Tooltip("AI�� ������ ��������Ʈ �������Դϴ�.")]
    public Transform[] waypoints;

    [Tooltip("�� ������ ���� �� ����� �ð�(��)�Դϴ�.")]
    public float waitTime = 1.0f;

    private NavMeshAgent agent;
    private Animator animator;
    private int currentWaypointIndex = 0;
    private bool isWaiting = false;
    private float _recoverCooldown = 0f;

    // [�߰�] ������(1->4)���� ������(4->1)���� üũ�ϴ� ����
    private bool movingForward = true;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        // waypoints가 없거나, 배열은 있어도 실제 참조가 없으면 비활성화
        if (waypoints == null || waypoints.Length == 0 || waypoints[0] == null)
        {
            enabled = false;
            return;
        }

        MoveToNextWaypoint();
    }

    void Update()
    {
        if (waypoints.Length == 0 || isWaiting) return;

        if (_recoverCooldown > 0f)
        {
            _recoverCooldown -= Time.deltaTime;
            return;
        }

        if (!agent.isOnNavMesh)
        {
            RecoverToNavMesh();
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            StartCoroutine(WaitAndMove());
        }
    }

    private void RecoverToNavMesh()
    {
        _recoverCooldown = 3f;
        NavMeshHit hit;
        if (waypoints.Length > 0 && waypoints[0] != null)
        {
            if (NavMesh.SamplePosition(waypoints[0].position, out hit, 20f, NavMesh.AllAreas))
            {
                agent.enabled = false;
                transform.position = hit.position;
                agent.enabled = true;
            }
        }
    }

    void MoveToNextWaypoint()
    {
        // 1. ���� �ε����� �̵� ����
        agent.destination = waypoints[currentWaypointIndex].position;

        // �ִϸ��̼� �ѱ�
        if (animator != null)
        {
            animator.SetBool("IsMoving", true);
        }

        // 2. [������] ���� �ε��� ��� (�պ� ����)
        if (movingForward)
        {
            // ������ �̵� ���̶�� �ε��� ����
            if (currentWaypointIndex >= waypoints.Length - 1)
            {
                // ������ ������ �����ߴٸ� ������ ������ �ε��� ����
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
            // ������ �̵� ���̶�� �ε��� ����
            if (currentWaypointIndex <= 0)
            {
                // ���� ������ �����ߴٸ� ������ �ٽ� ���������� �ϰ� �ε��� ����
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