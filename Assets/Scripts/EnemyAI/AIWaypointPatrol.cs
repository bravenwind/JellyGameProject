using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Photon.Pun;

public class AIWaypointPatrol : MonoBehaviourPun, IPunObservable
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

        _isMine = NetworkNavMeshHelper.SetupOwnership(this, agent,
            ref _networkPosition, ref _networkRotation);

        if (_isMine)
            MoveToNextWaypoint();
    }

    void Update()
    {
        if (!_isMine)
        {
            NetworkNavMeshHelper.InterpolateRemote(transform, _networkPosition, _networkRotation);
            if (animator != null)
                animator.SetBool("IsMoving", _networkIsMoving);
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
