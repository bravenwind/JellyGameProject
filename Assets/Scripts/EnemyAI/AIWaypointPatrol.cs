using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Photon.Pun;
using Photon.Realtime;

public class AIWaypointPatrol : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Settings")]
    public Transform[] waypoints;
    public float waitTime = 1.0f;

    private NavMeshAgent agent;
    private Animator animator;
    private int currentWaypointIndex = 0;
    private bool isWaiting = false;
    private bool movingForward = true;
    private bool _isMine;

    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    private bool _networkIsMoving;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (waypoints == null || waypoints.Length == 0 || waypoints[0] == null)
        {
            enabled = false;
            return;
        }

        _manualInterp = NetworkNavMeshHelper.NeedsManualInterp(this);
        _lastPos = transform.position;

        _isMine = NetworkNavMeshHelper.SetupOwnership(this, agent,
            ref _networkPosition, ref _networkRotation);

        if (_isMine)
            MoveToNextWaypoint();
    }

    // [LAN 이식] WanderingAI와 동일 — NetTransform이 있으면 위치는 건드리지 않는다.
    private bool _manualInterp;
    private Vector3 _lastPos;

    void Update()
    {
        if (!_isMine)
        {
            if (_manualInterp)
            {
                NetworkNavMeshHelper.InterpolateRemote(transform, _networkPosition, _networkRotation);
                if (animator != null) animator.SetBool("IsMoving", _networkIsMoving);
            }
            else if (animator != null)
            {
                animator.SetBool("IsMoving",
                    NetworkNavMeshHelper.MeasureMoving(transform, ref _lastPos));
            }
            return;
        }

        if (waypoints.Length == 0 || isWaiting) return;
        if (!agent.isOnNavMesh) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            StartCoroutine(WaitAndMove());
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        NetworkNavMeshHelper.SerializeTransform(stream, transform, agent,
            ref _networkPosition, ref _networkRotation, ref _networkIsMoving);
    }

    // [JL-3] 마스터 교체 시 소유권 재평가. 이게 없으면 _isMine이 Start의 1회 값에 굳어,
    // 새 마스터가 돼도 agent가 비활성인 채 순찰이 재개되지 않고 정지 좌표만 브로드캐스트된다.
    // (WanderingAI와 동일 패턴 — 획득/상실 양방향 재평가)
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (agent == null || waypoints == null || waypoints.Length == 0) return;
        if (photonView.IsMine == _isMine) return;

        _isMine = NetworkNavMeshHelper.SetupOwnership(this, agent,
            ref _networkPosition, ref _networkRotation);

        if (_isMine)
        {
            if (!agent.enabled) agent.enabled = true;
            MoveToNextWaypoint();
        }
    }

    void MoveToNextWaypoint()
    {
        agent.destination = waypoints[currentWaypointIndex].position;

        if (animator != null)
            animator.SetBool("IsMoving", true);

        if (movingForward)
        {
            if (currentWaypointIndex >= waypoints.Length - 1)
            {
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
            if (currentWaypointIndex <= 0)
            {
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
            animator.SetBool("IsMoving", false);

        yield return new WaitForSeconds(waitTime);

        agent.isStopped = false;
        isWaiting = false;

        MoveToNextWaypoint();
    }
}
