using UnityEngine;

/// <summary>
/// 조종을 잃은 캐릭터를 물리에 넘긴다 — 사람·봇 공용.
///
/// ★ 왜 필요한가
///   평소 캐릭터는 물리로 움직이지 않는다. 사람은 CharacterController가, 봇은
///   NavMeshAgent가 위치를 직접 쓴다. 프리팹의 Rigidbody는 kinematic이라
///   <b>트리거 이벤트를 받기 위한 장치</b>일 뿐 시뮬레이션에 참여하지 않는다.
///
///   그런데 발판이 사라지거나 탈락하면 '떨어지는 그림'이 필요하다. 그때만
///   Rigidbody를 깨워 물리에 맡긴다. 그래야 초콜릿의 부력·점성도 받는다.
///
/// ★ 예전엔 봇만 이 전환을 했다
///   사람은 탈락해도 CharacterController를 켠 채였고, CharacterController는
///   Rigidbody 물리를 <b>완전히 무시한다.</b> 그래서 초콜릿에 빠진 사람은
///   부력을 못 받고 그냥 가라앉았다 — 같은 초콜릿에서 봇은 둥둥 떠 있는데.
///   관전 화면에서 둘이 다르게 보이던 원인이다.
/// </summary>
public static class PhysicsFall
{
    /// <summary>
    /// 이 오브젝트를 물리 낙하 상태로 바꾼다.
    /// 조종하던 것(CharacterController·NavMeshAgent·FSM)을 끄는 일은 부르는 쪽이 한다 —
    /// 사람과 봇이 서로 다른 것을 조종하기 때문이다.
    /// </summary>
    public static Rigidbody Begin(GameObject go)
    {
        if (go == null)
            return null;

        Rigidbody rb = go.GetComponent<Rigidbody>();

        if (rb == null)
            rb = go.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;

        //빠르게 떨어지면 얇은 바닥을 통과할 수 있다. 스윕 판정으로 막는다
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        return rb;
    }
}
