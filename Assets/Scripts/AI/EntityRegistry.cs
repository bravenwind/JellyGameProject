using System.Collections.Generic;
using UnityEngine;
using JellyNet;

/// <summary>
/// 씬에 존재하는 플레이어/봇/젤리를 중앙에서 관리하는 정적 레지스트리.
///
/// [순회 안전성 설계]
///   내부 저장은 HashSet(중복 방지 + 빠른 추가/삭제)을 사용하지만,
///   HashSet은 foreach 순회 도중 Add/Remove가 일어나면 InvalidOperationException을 던진다.
///   멀티플레이에서 한 프레임에 여러 엔티티가 파괴되면(OnDisable → Unregister)
///   AI 탐지 루프 순회 중에 실제로 터질 수 있다.
///
///   이를 막기 위해 외부에는 "스냅샷 List"를 노출한다.
///   - Register/Unregister 시 dirty 플래그만 세운다 (스냅샷은 건드리지 않음).
///   - 외부에서 컬렉션에 접근할 때, dirty면 새 List를 1회 생성해 캐싱한다.
///   - 새 List 인스턴스를 만들기 때문에, 순회 도중 엔티티가 사라져도
///     이미 진행 중인 foreach는 옛 스냅샷을 그대로 돌아 안전하다.
///   변경이 없으면 스냅샷을 재사용하므로 매 프레임 GC 할당도 발생하지 않는다.
/// </summary>
public static class EntityRegistry
{
    // 플레이어의 기준 컴포넌트는 LanPlayerState다. 등록 경로가 그 OnEnable 하나뿐이라,
    // 프리팹에서 빠지면 Players가 항상 0개가 된다.
    // 링 붕괴·AI 탐지·점수판·화면밖 표시가 전부 이 목록을 순회하므로,
    // 그때는 "고장"이 아니라 "아무 일도 안 일어남"으로 나타나 원인이 잘 안 보인다.
    private static readonly HashSet<LanPlayerState> players = new HashSet<LanPlayerState>();
    private static readonly HashSet<JellyObject> jellies = new HashSet<JellyObject>();

    // 외부 순회용 스냅샷 (dirty일 때만 재생성)
    private static List<LanPlayerState> playersSnapshot = new List<LanPlayerState>();
    private static List<JellyObject> jelliesSnapshot = new List<JellyObject>();

    private static bool playersDirty = true;
    private static bool jelliesDirty = true;

    /// <summary>
    /// <b>사람만.</b> "이 방에 사람이 몇 명인가", "이름 패킷을 어느 캐릭터에 꽂나"처럼
    /// 봇이 끼면 답이 틀리는 질문에만 쓴다.
    /// 사람이든 봇이든 상관없는 질문은 전부 <see cref="Entities"/>다.
    /// </summary>
    public static IReadOnlyList<LanPlayerState> Players
    {
        get
        {
            if (playersDirty)
            {
                playersSnapshot = new List<LanPlayerState>(players);
                playersDirty = false;
            }
            return playersSnapshot;
        }
    }

    public static IReadOnlyList<JellyObject> Jellies
    {
        get
        {
            if (jelliesDirty)
            {
                jelliesSnapshot = new List<JellyObject>(jellies);
                jelliesDirty = false;
            }
            return jelliesSnapshot;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  참가자 = 사람 + 봇
    // ─────────────────────────────────────────────────────────
    //
    // ★ 왜 따로 두나
    //   순위표·표적 선정·탈락 판정처럼 "사람이든 봇이든 상관없는" 질문이 많다.
    //   그런 곳이 Players 한 번, Bots 한 번 두 벌 루프를 돌고 있었고,
    //   두 벌이면 한쪽만 고쳐지는 일이 반드시 생긴다(봇 점수 미방송 버그가 그랬다).
    //   INetEntity로 묶어 한 벌로 돈다.
    //
    //   봇의 INetEntity 구현체는 AIPlayerMovement가 아니라 LanBotState다 —
    //   사람 쪽 짝(LanPlayerState)과 층을 맞추기 위해서다. 그래서 등록도 거기서 한다.
    //
    //   예전엔 Bots 목록이 따로 있었는데 담는 타입이 AIPlayerMovement(두뇌)라
    //   Players(LanPlayerState, 상태)와 층이 어긋났다. 그래서 같은 질문을 하면서도
    //   사람은 p.ScaleValue, 봇은 b.GetMyAuthorityScale()처럼 경로가 갈렸고
    //   조건 하나가 한쪽에만 빠지는 사고가 반복됐다. 목록을 지워서 갈라질 자리를 없앴다.
    private static readonly HashSet<INetEntity> entities = new HashSet<INetEntity>();
    private static List<INetEntity> entitiesSnapshot = new List<INetEntity>();
    private static bool entitiesDirty = true;

    public static IReadOnlyList<INetEntity> Entities
    {
        get
        {
            if (entitiesDirty)
            {
                entitiesSnapshot = new List<INetEntity>(entities);
                entitiesDirty = false;
            }
            return entitiesSnapshot;
        }
    }

    //사람은 Players에도 들어가야 해서 아래 전용 오버로드를 거치고,
    //봇은 따로 담을 목록이 없으므로 LanBotState가 이걸 직접 부른다
    public static void Register(INetEntity e)
    {
        if (entities.Add(e))
            entitiesDirty = true;
    }

    public static void Unregister(INetEntity e)
    {
        if (entities.Remove(e))
            entitiesDirty = true;
    }

    public static void Register(LanPlayerState p)
    {
        if (players.Add(p))
            playersDirty = true;
        Register((INetEntity)p);
    }

    public static void Unregister(LanPlayerState p)
    {
        if (players.Remove(p))
            playersDirty = true;
        Unregister((INetEntity)p);
    }

    public static void Register(JellyObject j)
    {
        if (jellies.Add(j))
            jelliesDirty = true;
    }

    public static void Unregister(JellyObject j)
    {
        if (jellies.Remove(j))
            jelliesDirty = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        players.Clear();
        jellies.Clear();
        entities.Clear();
        playersDirty = true;
        jelliesDirty = true;
        entitiesDirty = true;
    }
}
