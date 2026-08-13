using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public abstract class JellyAgentAI : MonoBehaviourPunCallbacks, IPunObservable
{
    protected const float MOVING_SPEED = 0.1f;
    private const float SPAWN_SNAP_RADIUS = 8f;
    private const float RECOVER_SNAP_RADIUS = 5f;

    protected NavMeshAgent agent;
    protected Animator anim;

    protected bool isMine;
    protected bool isWaiting;

    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private bool networkIsMoving;

    //LAN에서는 NetTransform이 위치를 몬다. 여기서 또 Lerp하면 두 시스템이 서로를 끌어당겨 젤리가 떤다
    private bool manualInterp;
    private Vector3 lastPos;

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = ResolveAnimator();
    }

    protected virtual Animator ResolveAnimator()
    {
        return GetComponent<Animator>();
    }

    //움직일 준비가 안 된 상태(순찰 지점 미지정 등)를 하위에서 알린다
    protected virtual bool IsReady()
    {
        return true;
    }

    protected virtual void Start()
    {
        if (!IsReady())
        {
            enabled = false;
            return;
        }

        manualInterp = NetworkNavMeshHelper.NeedsManualInterp(this);
        lastPos = transform.position;

        ClaimOwnership();
    }

    private void ClaimOwnership()
    {
        isMine = NetworkNavMeshHelper.SetupOwnership(this, agent,
            ref networkPosition, ref networkRotation);

        if (!isMine)
            return;

        //스폰 위치가 조금이라도 어긋났으면 유니티가 agent를 꺼놓는다
        //그대로 두면 이동 로직이 전부 isOnNavMesh에서 막혀 첫 목적지를 영영 못 받는다
        if (!agent.enabled)
            agent.enabled = true;

        SnapToNavMesh(SPAWN_SNAP_RADIUS);

        OnBecameDriver();
    }

    //소유권이 '바뀐' 경우에만 재평가한다
    //얻는 쪽만 통과시키면 잃는 쪽은 isMine이 true로 굳어 스플릿브레인이 남는다
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (agent == null || !IsReady())
            return;

        if (photonView.IsMine == isMine)
            return;

        ClaimOwnership();
    }

    private void Update()
    {
        if (!isMine)
        {
            UpdateRemote();
            return;
        }

        //카운트다운·종료 중에는 젤리도 멈춘다. 다 같이 3·2·1 하는데 젤리만 먼저 뛰면 어색하다
        if (JellyNet.LanGameFlow.IsFrozen)
        {
            HoldStill();
            return;
        }

        SetMoving(agent.isOnNavMesh && agent.velocity.magnitude > MOVING_SPEED);

        //발판이 무너져 발밑 NavMesh가 carve되면 이동 로직이 전부 막힌다
        //가까운 지점으로 되돌린다. 주변에 아예 없으면 복구를 포기하고 그대로 둔다
        if (!agent.isOnNavMesh)
        {
            SnapToNavMesh(RECOVER_SNAP_RADIUS);
            return;
        }

        DriveUpdate();
    }

    private void UpdateRemote()
    {
        if (manualInterp)
        {
            NetworkNavMeshHelper.InterpolateRemote(transform, networkPosition, networkRotation);
            SetMoving(networkIsMoving);
            return;
        }

        //위치는 NetTransform이 몬다. 애니메이션만 실제 변위로 맞춘다
        SetMoving(NetworkNavMeshHelper.MeasureMoving(transform, ref lastPos));
    }

    private void HoldStill()
    {
        if (agent.enabled && agent.isOnNavMesh)
        {
            if (agent.hasPath)
                agent.ResetPath();

            agent.velocity = Vector3.zero;
        }

        SetMoving(false);
    }

    protected void SnapToNavMesh(float radius)
    {
        if (agent.isOnNavMesh)
            return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, radius, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    protected void SetMoving(bool moving)
    {
        if (anim != null)
            anim.SetBool("IsMoving", moving);
    }

    //구동 권한을 잡은 직후 1회. 첫 목적지를 여기서 정한다
    protected abstract void OnBecameDriver();

    //내가 모는 동안 매 프레임. NavMesh 위에 있는 것이 보장된 상태로 들어온다
    protected abstract void DriveUpdate();

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        NetworkNavMeshHelper.SerializeTransform(stream, transform, agent,
            ref networkPosition, ref networkRotation, ref networkIsMoving);
    }
}
