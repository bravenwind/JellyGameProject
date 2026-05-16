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

    // 원격 보간용
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    private bool _networkIsMoving;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        _isMine = photonView != null && photonView.IsMine;

        if (waypoints == null || waypoints.Length == 0 || waypoints[0] == null)
        {
            enabled = false;
            return;
        }

        if (_isMine)
        {
            MoveToNextWaypoint();

            if (photonView != null && !photonView.ObservedComponents.Contains(this))
                photonView.ObservedComponents.Add(this);
        }
        else
        {
            agent.enabled = false;
            _networkPosition = transform.position;
            _networkRotation = transform.rotation;
        }
    }

    void Update()
    {
        if (!_isMine)
        {
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * 8f);
            transform.rotation = Quaternion.Lerp(transform.rotation, _networkRotation, Time.deltaTime * 8f);
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
