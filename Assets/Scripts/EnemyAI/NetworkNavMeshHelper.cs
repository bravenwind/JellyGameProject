using UnityEngine;
using UnityEngine.AI;
using JellyNet;

//NavMeshAgent를 이 기계에서 돌릴지 판단하는 도우미
public static class NetworkNavMeshHelper
{
    /// <summary>
    /// 이 오브젝트를 이 기계에서 구동할 것인가(= NavMeshAgent를 돌릴 것인가).
    ///
    /// IsMine이 아니라 IsSimulatedHere를 쓴다 — 씬에 배치된 젤리는 OwnerId가 0이라
    /// IsMine이 어디서도 참이 아니고, 그러면 호스트조차 agent를 못 켜서 전부 얼어붙는다.
    /// </summary>
    public static bool IsDriver(Component owner)
    {
        NetIdentity id = owner.GetComponentInParent<NetIdentity>();

        //네트워크 오브젝트가 아니면(오프라인 배치 등) 그냥 로컬에서 돌린다
        return id == null || id.IsSimulatedHere;
    }

    /// <summary>이동 여부를 실제 변위로 측정한다(원격 애니메이션용).</summary>
    public static bool MeasureMoving(Transform t, ref Vector3 lastPos)
    {
        float dt = Time.deltaTime;
        float speed = dt > 0f ? Vector3.Distance(t.position, lastPos) / dt : 0f;
        lastPos = t.position;
        return speed > 0.1f;
    }

    /// <summary>구동 권한을 판정하고, 남의 것이면 agent를 꺼서 위치 싸움을 막는다.</summary>
    public static bool SetupOwnership(Component owner, NavMeshAgent agent)
    {
        bool isMine = IsDriver(owner);

        if (!isMine && agent != null)
            agent.enabled = false;

        return isMine;
    }
}
