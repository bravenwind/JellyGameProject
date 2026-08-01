using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

public static class NetworkNavMeshHelper
{
    public const float DefaultLerpSpeed = 8f;

    /// <summary>
    /// 이 오브젝트를 이 기계에서 구동할 것인가(= NavMeshAgent를 돌릴 것인가).
    ///
    /// ★ LAN 이식 시 여기가 조용히 무너져 있었다
    ///   PhotonView를 프리팹에서 걷어낸 뒤로 owner.photonView가 항상 null이 되어
    ///   isMine이 <b>모든 기계에서 false</b>가 됐다. 그러면 아래에서
    ///   agent.enabled = false가 실행되므로 젤리는 스폰 직후 NavMeshAgent가 꺼진 채
    ///   땅에 박혀 영영 멈춰 있는다. 호스트에서 Warp으로 안착시켜 놔도
    ///   Start()가 그 뒤에 돌면서 다시 꺼버린다.
    ///
    ///   이제 NetIdentity를 먼저 본다. 없을 때만 예전 Photon 경로로 떨어진다.
    /// </summary>
    public static bool IsDriver(Component owner)
    {
        // ★ IsMineOrOffline이 아니라 IsSimulatedHere를 쓴다.
        //   씬에 배치된 젤리는 OwnerId가 0이라 IsMine이 어디서도 참이 아니다.
        //   그 상태로는 호스트조차 agent를 못 켜서 씬 젤리가 전부 얼어붙는다.
        JellyNet.NetIdentity id = owner.GetComponentInParent<JellyNet.NetIdentity>();
        if (id != null) return id.IsSimulatedHere;

        MonoBehaviourPun pun = owner as MonoBehaviourPun;
        return pun != null && pun.photonView != null && pun.photonView.IsMine;
    }

    /// <summary>
    /// 원격 오브젝트의 위치를 이 스크립트가 직접 몰아야 하는가.
    /// LAN에서는 NetTransform이 이미 위치를 담당하므로 손대면 두 시스템이 싸운다.
    /// </summary>
    public static bool NeedsManualInterp(Component owner)
    {
        return owner.GetComponentInParent<JellyNet.NetTransform>() == null;
    }

    /// <summary>이동 여부를 실제 변위로 측정한다(원격 애니메이션용).</summary>
    public static bool MeasureMoving(Transform t, ref Vector3 lastPos)
    {
        float dt = Time.deltaTime;
        float speed = dt > 0f ? Vector3.Distance(t.position, lastPos) / dt : 0f;
        lastPos = t.position;
        return speed > 0.1f;
    }

    public static bool SetupOwnership(MonoBehaviourPun owner, NavMeshAgent agent,
        ref Vector3 networkPos, ref Quaternion networkRot)
    {
        bool isMine = IsDriver(owner);

        // [JL-1] 관측 컴포넌트 등록은 소유·비소유 양쪽 모두에서 동일하게 수행한다.
        // 일부 젤리 프리팹은 PhotonView.ObservedComponents가 비어 있어(자산 손상: [] 또는 [{fileID:0}])
        // 이 컴포넌트의 OnPhotonSerializeView가 아예 호출되지 않았고, 그 결과 비마스터 화면에서 위치
        // 스트림이 최초 raw 좌표(리프트 안 된 임베드 위치)에서 갱신되지 않아 젤리가 땅에 박힌 채
        // 영구 정지했다. writer(소유자)만 등록하고 reader(비소유자)가 등록 안 하면, reader는 들어온
        // 스트림을 읽어 넣을 관측 슬롯이 없어 _networkPosition이 갱신되지 않는다. 모든 클라가 동일하게
        // 자가 등록해야 writer/reader의 직렬화 순서가 일치한다. (프리팹이 정상이면 Contains로 중복 방지)
        if (owner.photonView != null
            && owner.photonView.ObservedComponents != null
            && !owner.photonView.ObservedComponents.Contains(owner))
        {
            owner.photonView.ObservedComponents.Add(owner);
        }

        if (!isMine)
        {
            agent.enabled = false;
            networkPos = owner.transform.position;
            networkRot = owner.transform.rotation;
        }

        return isMine;
    }

    public static void InterpolateRemote(Transform t, Vector3 targetPos, Quaternion targetRot,
        float speed = DefaultLerpSpeed)
    {
        t.position = Vector3.Lerp(t.position, targetPos, Time.deltaTime * speed);
        t.rotation = Quaternion.Lerp(t.rotation, targetRot, Time.deltaTime * speed);
    }

    public static void SerializeTransform(PhotonStream stream, Transform t, NavMeshAgent agent,
        ref Vector3 networkPos, ref Quaternion networkRot, ref bool networkIsMoving)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(t.position);
            stream.SendNext(t.rotation);
            stream.SendNext(agent.isOnNavMesh && agent.velocity.magnitude > 0.1f);
        }
        else
        {
            networkPos = (Vector3)stream.ReceiveNext();
            networkRot = (Quaternion)stream.ReceiveNext();
            networkIsMoving = (bool)stream.ReceiveNext();
        }
    }
}
