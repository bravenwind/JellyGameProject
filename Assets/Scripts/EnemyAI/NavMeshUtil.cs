using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh를 다룰 때 프로젝트 전체가 공유하는 상수·계산.
///
/// ★ 예전 이름은 NetworkNavMeshHelper였다
///   "이 기계가 이 agent를 굴리는가"를 판단하는 IsDriver / SetupOwnership이 여기 있었는데,
///   둘 다 Component를 받아 GetComponentInParent&lt;NetIdentity&gt;로 계층을 거슬러 올라갔다.
///   그 판단은 NetIdentity를 이미 들고 있는 쪽이 하는 게 맞아서 호출부로 돌려보냈다.
///   여기 남은 것은 "누가 부르든 답이 같은" 계산뿐이다.
/// </summary>
public static class NavMeshUtil
{
    private static int walkableMask;

    /// <summary>
    /// 걸어다닐 수 있는 영역만. NavMesh에는 Walkable과 Jump만 구워진다
    /// ("Not Walkable"은 애초에 제외된다). Jump는 지나가는 링크지 서 있을 자리가 아니다.
    ///
    /// ★ static 필드 초기화로 두면 안 된다
    ///   NavMesh.GetAreaFromName은 MonoBehaviour 생성 중에 부를 수 없는데,
    ///   static 필드 초기화는 이 타입을 처음 건드리는 순간(=그 시점일 수 있다) 실행된다.
    ///   그래서 '처음 읽을 때' 채운다. 마스크는 1 &lt;&lt; n 이라 0이 될 수 없어 0을 미초기화로 쓴다.
    /// </summary>
    public static int WalkableMask
    {
        get
        {
            if (walkableMask == 0)
                walkableMask = 1 << NavMesh.GetAreaFromName("Walkable");
            return walkableMask;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWalkableMask() => walkableMask = 0;

    /// <summary>이 agent가 걸어다닐 수 있는 영역만 보는 질의 필터.</summary>
    public static NavMeshQueryFilter WalkableFilter(NavMeshAgent agent)
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask    = WalkableMask
        };
    }

    /// <summary>이동 여부를 실제 변위로 측정한다(원격 개체의 걷는 애니메이션용).</summary>
    public static bool MeasureMoving(Transform t, ref Vector3 lastPos)
    {
        float dt = Time.deltaTime;
        float speed = dt > 0f ? Vector3.Distance(t.position, lastPos) / dt : 0f;
        lastPos = t.position;
        return speed > 0.1f;
    }
}
