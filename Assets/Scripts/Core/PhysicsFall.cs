using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 조종을 잃은 것을 물리에 넘긴다 — 사람·봇·소품 공용.
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
///
/// ★ 조종 장치를 끄는 일도 여기서 한다 (예전엔 부르는 쪽에 맡겼다)
///   "사람과 봇이 서로 다른 것을 조종하니 호출부가 알아서 끄라"고 나눠놨었다.
///   그 결과 같은 일을 하는 코드가 세 벌로 갈라졌고, 셋이 서로 달랐다:
///
///     FallingTile   — WanderingAI·NavMeshAgent를 <b>자식까지</b> 끄고, Begin을 아예 안 쓰고
///                     isKinematic/useGravity/collisionDetectionMode를 손으로 다시 적었다
///     ChocolateFluid— WanderingAI·NavMeshAgent를 <b>루트만</b> 끈다 (자식에 달리면 못 끔)
///     LanPlayerState— PlayerMovement·CharacterController를 끈다
///
///   "서로 다른 것을 조종한다"는 <b>있으면 끈다</b> 몇 줄로 끝나는 문제였지,
///   호출부마다 나눠줄 문제가 아니었다. 여기서 한 번에 끄면 세 벌이 한 벌이 된다.
///
///   ※ AI의 FSM 상태 종료(currentState.Exit)만은 여기서 못 한다 — 상태 객체는
///     AIPlayerMovement 안에만 있다. 그쪽은 StopBrain()이 계속 담당한다.
/// </summary>
public static class PhysicsFall
{
    /// <summary>
    /// 이 오브젝트를 물리에 넘긴다.
    /// 조종 장치를 끄고, Rigidbody를 dynamic으로 돌리고, 잠들어 있으면 깨운다.
    /// </summary>
    /// <param name="useGravity">
    /// 중력을 켤지. 기본은 켠다(떨어져야 하니까).
    ///
    /// ★ 인자로 받는 이유
    ///   초콜릿 안에서는 중력을 꺼야 한다 — 부력이 대신하기 때문이다. 예전엔 여기서
    ///   무조건 켜고 ChocolateFluid가 <b>바로 다음 줄에서 다시 껐다.</b> 동작은 맞지만
    ///   "중력을 누가 정하는가"가 두 곳이 되고, 읽는 사람은 켰다 끄는 이유를 찾게 된다.
    ///   부르는 쪽이 한 번에 말하게 한다.
    /// </param>
    /// <returns>물리를 맡게 된 Rigidbody. go가 null이면 null.</returns>
    public static Rigidbody Begin(GameObject go, bool useGravity = true)
    {
        if (go == null)
            return null;

        ReleaseControllers(go);

        Rigidbody rb = go.GetComponent<Rigidbody>();

        if (rb == null)
            rb = go.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = useGravity;

        //빠르게 떨어지면 얇은 바닥을 통과할 수 있다. 스윕 판정으로 막는다
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // ★ 깨우지 않으면 발판이 사라져도 공중에 멈춰 있는다
        //   가만히 서 있던 Rigidbody는 물리 엔진이 재우는데, 바닥 콜라이더를 끄는 건
        //   충돌 이벤트가 아니라서 저절로 깨어나지 않는다. FallingTile은 이걸 알고
        //   따로 WakeUp을 부르고 있었다 — 같은 함정이 사람·봇에게도 있으므로 여기로 옮긴다.
        rb.WakeUp();

        return rb;
    }

    /// <summary>
    /// 이 오브젝트를 몰고 있던 것을 끈다. 하나도 없으면(소품) 아무 일도 일어나지 않는다.
    ///
    /// ★ 복수형(GetComponentsInChildren)이 아니라 단수형이다
    ///   넷 다 <b>개체당 하나씩</b>이다 — 프리팹을 전수 확인했다:
    ///     NavMeshAgent 18개, WanderingAI 16개, CharacterController 3개, PlayerMovement 1개.
    ///     전부 루트에 하나씩이고, 자식에 달린 것도 둘 이상인 것도 없다.
    ///   복수형은 호출마다 배열을 새로 할당하므로 낙하 한 번에 배열이 넷 생겼다.
    ///
    ///   그래도 GetComponent(루트만)가 아니라 InChildren인 건 인자 true 때문이다 —
    ///   비활성 가지에 남은 agent도 확실히 끈다. 단수형은 첫 하나를 찾으면 멈추고
    ///   배열도 만들지 않으므로 그 안전망이 공짜로 딸려온다.
    /// </summary>
    private static void ReleaseControllers(GameObject go)
    {
        //CharacterController는 Rigidbody 물리를 완전히 무시한다. 반드시 먼저 꺼야 한다.
        CharacterController controller = go.GetComponentInChildren<CharacterController>(true);
        if (controller != null)
            controller.enabled = false;

        PlayerMovement movement = go.GetComponentInChildren<PlayerMovement>(true);
        if (movement != null)
            movement.enabled = false;

        //AI 스크립트를 먼저 끄지 않으면 다음 프레임에 스스로 agent를 다시 켠다.
        WanderingAI wandering = go.GetComponentInChildren<WanderingAI>(true);
        if (wandering != null)
            wandering.enabled = false;

        NavMeshAgent agent = go.GetComponentInChildren<NavMeshAgent>(true);
        if (agent != null)
            agent.enabled = false;
    }
}
