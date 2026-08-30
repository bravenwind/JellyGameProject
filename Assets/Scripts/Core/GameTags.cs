using UnityEngine;

/// <summary>
/// 씬 설정(Tags &amp; Layers)과 애니메이터에 적어둔 이름들을 코드에서 한 곳으로 모은 것.
///
/// ★ 왜 모으나
///   이 이름들은 <b>문자열이라 오타가 컴파일에 안 걸린다.</b> 틀리면 조용히
///   "아무것도 안 일어남"이 되고, 그건 고장보다 찾기 어렵다.
///   실제로 이 프로젝트에서 "Edible"은 8곳, "IsMoving"은 9곳에 흩어져 있었다.
///
///   한 곳에 모아두면
///     · 오타가 나면 <b>그 자리에서 컴파일 에러</b>가 난다
///     · Unity 쪽에서 이름을 바꿀 때 고칠 데가 한 곳이다
///     · 어떤 이름이 있는지 목록으로 보인다
///
/// ★ 값을 바꿀 때는 반드시 Unity 설정도 같이 고칠 것
///   여기 문자열은 Project Settings &gt; Tags and Layers, 그리고 Animator Controller의
///   파라미터 이름과 <b>글자 단위로 같아야 한다.</b>
/// </summary>
public static class GameTags
{
    // ─────────────────────────────────────────────────────────
    //  태그 (Project Settings > Tags and Layers > Tags)
    // ─────────────────────────────────────────────────────────

    /// <summary>먹을 수 있는 젤리. JellyColliderAbsorb가 런타임에 렌더러 오브젝트에 붙인다.</summary>
    public const string Edible = "Edible";

    /// <summary>캐릭터 몸통 메시. 사람·봇 프리팹의 자식 Object001에 붙어 있다.</summary>
    public const string PlayerMesh = "PlayerMesh";

    // ═════════════════════════════════════════════════════════
    //  캐릭터의 '대표' 콜라이더
    // ═════════════════════════════════════════════════════════
    //
    // ★ 캐릭터에는 트리거 콜라이더가 두 개 붙어 있다
    //   루트의 CapsuleCollider(isTrigger)와 자식 Object001의 MeshCollider(isTrigger).
    //   그래서 캐릭터가 어딘가에 들어가면 <b>OnTrigger*가 개체당 두 번</b> 불린다.
    //
    //   이걸 안 걸면 판정이 두 배로 돈다. 실제로 났던 일들:
    //     · 초콜릿의 부력·흐름이 두 배로 들어갔다. 힘을 FixedUpdate로 옮기자
    //       이번엔 정확히 반토막이 났다 — 원인은 늘 이 이중 호출이었다.
    //     · BatArcQuery의 hitBuffer를 32로 키워야 했다(캐릭터당 2칸을 먹으므로).
    //     · 봇의 흡수 판정이 같은 상대에게 두 번 돌았다.
    //
    //   Milk와 PuddingWiggle은 예전부터 PlayerMesh 태그로 하나만 골라 받고 있었다.
    //   그 규칙을 전부에 적용해 통로를 하나로 만든다.
    //
    //   ※ 콜라이더를 지워서 해결하지 않는 이유: 메시 쪽이 몸 모양을 따라가 지형 효과의
    //     체감이 낫고, 프리팹을 건드리면 되돌리기가 어렵다. 규칙만 세우면 코드로 끝난다.

    /// <summary>이 콜라이더가 캐릭터의 대표 콜라이더인가. 캐릭터 판정은 반드시 이걸로 한 번만 받는다.</summary>
    public static bool IsCharacterProxy(Collider c)
    {
        return c != null && c.CompareTag(PlayerMesh);
    }

    /// <summary>굴러다니는 사탕 소품.</summary>
    public const string Sphere = "Sphere";

    /// <summary>배경 소품(초콜릿에 떠다니는 것들).</summary>
    public const string BackGroundObject = "BackGroundObject";

    /// <summary>미니맵을 비추는 카메라. MinimapArrowManager가 태그로 찾는다.</summary>
    public const string MinimapCamera = "MinimapCamera";

    /// <summary>Unity 내장 태그. Camera.main이 이걸 본다.</summary>
    public const string MainCamera = "MainCamera";
}

/// <summary>
/// 레이어 이름과, 그걸 마스크로 바꾼 값의 캐시.
///
/// ★ NameToLayer는 매번 부르지 않는다
///   문자열 조회라 공짜가 아닌데, 답은 실행 중에 바뀌지 않는다.
///   처음 읽을 때 한 번만 구해두고 재사용한다.
///
/// ★ 없는 레이어는 -1이 온다 — 그대로 시프트하면 안 된다
///   `1 &lt;&lt; -1` 은 C#에서 `1 &lt;&lt; 31`이 되어 <b>엉뚱한 31번 레이어</b>가 마스크에 섞인다.
///   여기서 걸러 0(아무것도 안 맞음)을 돌려준다.
/// </summary>
public static class GameLayers
{
    public const string PlayerName = "Player";
    public const string EdibleName = "Edible";
    public const string ChocolateName = "Chocolate";
    public const string GroundName = "Ground";
    public const string MinimapName = "Minimap";
    public const string BackGroundObjectName = "BackGroundObject";

    private static int player = Unset;
    private static int edible = Unset;
    private static int backGroundObject = Unset;
    private static int ground = Unset;

    private const int Unset = -2;   //-1은 '그런 레이어 없음'이라 미조회 표시로 못 쓴다

    /// <summary>사람·봇이 올라가는 레이어.</summary>
    public static int Player => Cached(ref player, PlayerName);

    /// <summary>젤리 프리팹이 올라가는 레이어.</summary>
    public static int Edible => Cached(ref edible, EdibleName);

    public static int BackGroundObject => Cached(ref backGroundObject, BackGroundObjectName);

    /// <summary>발판(Tile_x_z)이 올라가는 레이어.</summary>
    public static int Ground => Cached(ref ground, GroundName);

    /// <summary>사람·봇만 담은 마스크. 배트 판정처럼 캐릭터만 훑을 때 쓴다.</summary>
    public static int PlayerMask => MaskOf(Player);

    public static int EdibleMask => MaskOf(Edible);

    public static int BackGroundObjectMask => MaskOf(BackGroundObject);

    /// <summary>"밟고 설 수 있는 것"의 마스크. 발판과 그 위에 놓인 소품.</summary>
    public static int StandableMask => MaskOf(Ground) | MaskOf(BackGroundObject);

    private static int Cached(ref int slot, string name)
    {
        if (slot == Unset)
            slot = LayerMask.NameToLayer(name);
        return slot;
    }

    /// <summary>레이어 번호를 마스크로. 없는 레이어(-1)면 0을 돌려준다.</summary>
    public static int MaskOf(int layer)
    {
        return layer >= 0 ? 1 << layer : 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        player = Unset;
        edible = Unset;
        backGroundObject = Unset;
        ground = Unset;
    }
}

/// <summary>
/// 애니메이터 파라미터 이름과, 그걸 해시로 바꾼 값.
///
/// 문자열 대신 해시로 넘기면 매 호출의 문자열 비교가 사라진다.
/// Animator.StringToHash는 static 필드 초기화에서 불러도 되는 몇 안 되는 API다
/// (Shader.PropertyToID와 같은 부류).
/// </summary>
public static class AnimParams
{
    public const string IsMovingName = "IsMoving";
    public const string JumpName = "Jump";
    public const string DashName = "Dash";
    public const string AttackName = "Attack";
    public const string HitName = "Hit";
    public const string WiggleName = "Wiggle";

    public static readonly int IsMoving = Animator.StringToHash(IsMovingName);
    public static readonly int Jump = Animator.StringToHash(JumpName);
    public static readonly int Dash = Animator.StringToHash(DashName);
    public static readonly int Attack = Animator.StringToHash(AttackName);
    public static readonly int Hit = Animator.StringToHash(HitName);
    public static readonly int Wiggle = Animator.StringToHash(WiggleName);
}
