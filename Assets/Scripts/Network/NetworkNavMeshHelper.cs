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

        if (isMine)
        {
            if (owner.photonView != null && !owner.photonView.ObservedComponents.Contains(owner))
                owner.photonView.ObservedComponents.Add(owner);
        }
        else
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
