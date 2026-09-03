using UnityEngine;
using JellyNet;

/// <summary>
/// 성장 팝업 프리팹들을 풀링하고 띄우는 컨테이너.
/// 사람·봇 프리팹 <b>둘 다</b>의 자식(LevelUpFloaterPool)에 있다.
/// 캐릭터가 자랄 때마다 등록된 프리팹 중 하나를 무작위로 골라 그 종류의 풀에서 꺼내 재생한다.
///
/// ★ 꺼내고 넣는 규칙은 직접 만들지 않는다 — ComponentPool에 맡긴다
///   예전엔 이 클래스가 Queue를 손으로 굴리며 Get/Create/Return을 다 갖고 있었다.
///   그 시절엔 이유가 있었다: 이 컴포넌트를 PlayerBridge가 런타임에 AddComponent로
///   만들었기 때문에 <b>복제할 프리팹을 인스펙터에서 받을 방법이 없었고</b>,
///   ComponentPool은 프리팹을 받아야 동작한다. 지금은 이 컨테이너가 캐릭터 프리팹의
///   자식으로 들어 있어 그 조건이 사라졌다.
///   ComponentPool은 UnityEngine.Pool.ObjectPool을 감싼 것이고, 그건 UI든 3D든
///   가리지 않는 순수 자료구조다(같은 클래스를 LanLeaderboardUI가 UI 행에 쓰고 있다).
///
/// ★ 종류별로 풀을 따로 둔다
///   프리팹이 여러 개인데 풀이 하나면, 반납된 A를 다음 B 자리에 꺼내 쓰게 된다.
///   그래서 pools[i]가 popupPrefabs[i]만 복제한다.
///
/// ★ 언제 뜰지는 이 컴포넌트가 직접 듣는다
///   예전엔 PlayerBridge가 받아서 풀에게 Play()를 시켰다. 그래서 <b>봇에는 팝업이
///   없었다</b> — 봇에 붙는 건 BotBridge이고 거기엔 그 구독이 없었다.
///   같은 코드를 BotBridge에도 복사하는 대신 풀이 스스로 듣는다.
///   이제 "풀 자식이 있는 캐릭터는 팝업이 뜬다"가 전부라 사람·봇이 같다.
///
/// ★ 크기 파이프라인이 아니라 <b>방송이 도착한 자리</b>를 듣는다
///   한때 PlayerScaleController.OnGrowStarted를 구독했다. 사람은 그래도 맞는데
///   (사람의 크기는 모든 기계가 스스로 만든다) 봇은 아니다 —
///   봇의 ScaleTo는 구동자에서만 도니까 클라 화면에서는 봇이 자라도 발화하지 않는다.
///   그래서 크기와 무관하게 기계당 정확히 한 번 오는 두 자리를 대신 듣는다:
///     · PlayerAbsorber.OnJellyScored           — 젤리 (EatJellyConfirm 방송)
///     · LanPlayerVisual.OnGrowBroadcastReceived — 봇 흡수·배트 적중 (GrowEvent 방송)
///   둘 다 개체별이고 모든 기계에서 불리므로, 원격 화면에서도 먹은 그 캐릭터 옆에 뜬다
///   (풀이 그 캐릭터의 자식이고 팝업도 자식으로 낳는다).
/// </summary>
public class LevelUpFloaterPool : MonoBehaviour
{
    [Tooltip("띄울 팝업 프리팹들. 이 중 하나가 무작위로 뜬다.")]
    [SerializeField] private LevelUpFloater[] popupPrefabs = new LevelUpFloater[3];

    [Tooltip("종류마다 미리 만들어둘 개수")]
    [SerializeField] private int prewarmPerPrefab = 2;

    private Transform scaleRef;   // 크기 상쇄 기준 = 캐릭터 루트(이 컨테이너의 부모)
    private PlayerAbsorber absorber;          // 젤리 흡수 방송이 도착하는 자리
    private LanPlayerVisual visual;           // 봇 흡수·배트 적중 방송이 도착하는 자리
    private ComponentPool<LevelUpFloater>[] pools;

    private void Awake()
    {
        scaleRef = transform.parent;
        absorber = GetComponentInParent<PlayerAbsorber>();
        visual = GetComponentInParent<LanPlayerVisual>();

        if (popupPrefabs == null)
            popupPrefabs = new LevelUpFloater[0];

        pools = new ComponentPool<LevelUpFloater>[popupPrefabs.Length];

        for (int i = 0; i < popupPrefabs.Length; i++)
        {
            if (popupPrefabs[i] == null)
                continue;   //아직 프리팹을 안 꽂은 칸. PickKind가 건너뛴다

            //자식으로 낳는다 — 캐릭터가 움직이면 팝업도 따라가고 사라질 때 같이 사라진다
            pools[i] = new ComponentPool<LevelUpFloater>(
                popupPrefabs[i], transform, prewarmPerPrefab);
        }
    }

    private void OnEnable()
    {
        if (absorber != null)
            absorber.OnJellyScored += Play;
        if (visual != null)
            visual.OnGrowBroadcastReceived += Play;
    }

    private void OnDisable()
    {
        if (absorber != null)
            absorber.OnJellyScored -= Play;
        if (visual != null)
            visual.OnGrowBroadcastReceived -= Play;
    }

    /// <summary>팝업 1회 표시. 동시에 여러 번 불려도 각 인스턴스가 독립적으로 뜬다.</summary>
    public void Play()
    {
        if (pools == null)
            return;

        if (scaleRef == null)
            scaleRef = transform.parent;

        int kind = PickKind();
        if (kind < 0)
            return;   //칸이 전부 비어 있으면 조용히 넘어간다

        LevelUpFloater floater = pools[kind].Get();
        if (floater == null)
            return;

        floater.Play(scaleRef, f => pools[kind].Return(f));
    }

    //비어 있는 칸(프리팹 미할당)을 건너뛰고 고른다.
    //전부 비었으면 -1. 시작 위치만 무작위로 잡고 한 바퀴 도는 방식이라
    //유효한 칸이 하나뿐이어도 무한 루프에 빠지지 않는다.
    private int PickKind()
    {
        int count = pools.Length;
        if (count == 0)
            return -1;

        int start = Random.Range(0, count);

        for (int step = 0; step < count; step++)
        {
            int i = (start + step) % count;
            if (pools[i] != null)
                return i;
        }
        return -1;
    }
}
