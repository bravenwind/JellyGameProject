using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Photon.Pun;

[RequireComponent(typeof(NavMeshAgent))]
public class WanderingAI : MonoBehaviourPun, IPunObservable
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

    // 원격 클라이언트용 보간 변수
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    private bool _networkIsMoving;
    private bool _isMine;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        _isMine = photonView != null && photonView.IsMine;

        if (_isMine)
        {
            agent.avoidancePriority = Random.Range(0, 100);
            initialPosition = transform.position;
            MoveToRandomPosition();

            // PhotonView에 이 컴포넌트를 observer로 등록
            if (photonView != null && !photonView.ObservedComponents.Contains(this))
                photonView.ObservedComponents.Add(this);
        }
        else
        {
            // 원격 클라이언트: AI와 NavMeshAgent 비활성화, 위치는 네트워크로 받음
            agent.enabled = false;
            _networkPosition = transform.position;
            _networkRotation = transform.rotation;
        }
    }

    void Update()
    {
        if (!_isMine)
        {
            // 원격: 보간으로 부드럽게 이동
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * 8f);
            transform.rotation = Quaternion.Lerp(transform.rotation, _networkRotation, Time.deltaTime * 8f);

            if (jellyAnimController != null)
                jellyAnimController.SetBool("IsMoving", _networkIsMoving);
            return;
        }

        // 로컬(소유자): AI 로직 실행
        bool isActuallyMoving = agent.isOnNavMesh && agent.velocity.magnitude > 0.1f;
        if (jellyAnimController != null)
            jellyAnimController.SetBool("IsMoving", isActuallyMoving);

        if (!agent.isOnNavMesh) return;
        if (isWaiting) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
            {
                StartCoroutine(WaitAndMove());
            }
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(agent.isOnNavMesh && agent.velocity.magnitude > 0.1f);
        }
        else
        {
            _networkPosition = (Vector3)stream.ReceiveNext();
            _networkRotation = (Quaternion)stream.ReceiveNext();
            _networkIsMoving = (bool)stream.ReceiveNext();
        }
    }

    IEnumerator WaitAndMove()
    {
        isWaiting = true;

        float waitTime = Random.Range(minWaitTime, maxWaitTime);
        yield return new WaitForSeconds(waitTime);

        MoveToRandomPosition();
        isWaiting = false;
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = Application.isPlaying && anchorToInitialPosition ? initialPosition : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}
