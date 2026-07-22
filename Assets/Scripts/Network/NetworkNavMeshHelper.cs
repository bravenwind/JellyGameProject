using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

public static class NetworkNavMeshHelper
{
    public const float DefaultLerpSpeed = 8f;

    public static bool SetupOwnership(MonoBehaviourPun owner, NavMeshAgent agent,
        ref Vector3 networkPos, ref Quaternion networkRot)
    {
        bool isMine = owner.photonView != null && owner.photonView.IsMine;

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
