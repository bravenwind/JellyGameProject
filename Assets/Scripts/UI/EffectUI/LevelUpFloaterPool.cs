using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 성장 팝업 프리팹들을 풀링하고 띄우는 컨테이너.
/// 사람·봇 프리팹 <b>둘 다</b>의 자식(LevelUpFloaterPool)에 있다.
/// 캐릭터가 자랄 때마다 등록된 프리팹 중 하나를 무작위로 골라 그 종류의 풀에서 꺼내 재생한다.
///
/// ★ 언제 뜰지는 이 컴포넌트가 직접 듣는다
///   예전엔 PlayerBridge가 OnGrowStarted를 받아 풀에게 Play()를 시켰다. 그래서
///   <b>봇에는 팝업이 없었다</b> — 봇에 붙는 건 BotBridge이고 거기엔 그 구독이 없었다.
///   같은 코드를 BotBridge에도 복사하는 대신, 풀이 스스로 부모의 PlayerScaleController를
///   구독한다. 이제 "풀 자식이 있는 캐릭터는 팝업이 뜬다"가 전부라 사람·봇이 같다.
///
/// ★ 네트워크: 팝업은 모든 화면에서, 그 캐릭터 옆에 뜬다
///   젤리 흡수는 호스트가 EatJellyConfirm을 전원에게 방송하고, 각 기계의
///   AbsorbMode.OnEatConfirmed가 <b>먹은 개체를 NetId로 찾아</b> PlayerAbsorber.AbsorbColor를
///   부른다 → GrowByJelly → OnGrowStarted. 풀은 그 캐릭터의 자식이고 팝업도 자식으로
///   낳으므로, 원격 화면에서도 먹은 그 캐릭터 옆에 뜬다.
///   봇 흡수·배트 적중은 GrowEvent 방송이 같은 자리로 이어진다.
///
/// ★ 종류별로 줄을 따로 세운다
///   프리팹이 여러 개인데 큐가 하나면, 반납된 A를 다음 B 자리에 꺼내 쓰게 된다.
///   그래서 free[i]가 popupPrefabs[i]의 인스턴스만 담는다.
/// </summary>
public class LevelUpFloaterPool : MonoBehaviour
{
    [Tooltip("띄울 팝업 프리팹들. 이 중 하나가 무작위로 뜬다.")]
    [SerializeField] private LevelUpFloater[] popupPrefabs = new LevelUpFloater[3];

    [Tooltip("종류마다 미리 만들어둘 개수")]
    [SerializeField] private int prewarmPerPrefab = 2;

    private Transform scaleRef;   // 크기 상쇄 기준 = 캐릭터 루트(이 컨테이너의 부모)
    private PlayerScaleController scaleController;
    private Queue<LevelUpFloater>[] free;

    private void Awake()
    {
        scaleRef = transform.parent;
        scaleController = GetComponentInParent<PlayerScaleController>();

        if (popupPrefabs == null)
            popupPrefabs = new LevelUpFloater[0];

        free = new Queue<LevelUpFloater>[popupPrefabs.Length];

        for (int i = 0; i < popupPrefabs.Length; i++)
        {
            free[i] = new Queue<LevelUpFloater>();
            if (popupPrefabs[i] == null)
                continue;
            for (int n = 0; n < prewarmPerPrefab; n++)
                free[i].Enqueue(Create(i));
        }
    }

    private void OnEnable()
    {
        if (scaleController != null)
            scaleController.OnGrowStarted += HandleGrowStarted;
    }

    private void OnDisable()
    {
        if (scaleController != null)
            scaleController.OnGrowStarted -= HandleGrowStarted;
    }

    //playEffect가 false인 성장(연출 없이 값만 맞추는 보정)에는 뜨지 않는다.
    private void HandleGrowStarted(bool playEffect)
    {
        if (playEffect)
            Play();
    }

    /// <summary>팝업 1회 표시. 동시에 여러 번 불려도 각 인스턴스가 독립적으로 뜬다.</summary>
    public void Play()
    {
        if (free == null)
            return;

        if (scaleRef == null)
            scaleRef = transform.parent;

        int kind = PickKind();
        if (kind < 0)
            return;   //칸이 전부 비어 있으면 조용히 넘어간다 — 아직 프리팹을 안 꽂은 상태다

        LevelUpFloater floater = Get(kind);
        if (floater == null)
            return;

        floater.Play(scaleRef, f => Return(kind, f));
    }

    //비어 있는 칸(프리팹 미할당)을 건너뛰고 고른다.
    //전부 비었으면 -1. 시작 위치만 무작위로 잡고 한 바퀴 도는 방식이라
    //유효한 칸이 하나뿐이어도 무한 루프에 빠지지 않는다.
    private int PickKind()
    {
        int count = popupPrefabs.Length;
        int start = Random.Range(0, Mathf.Max(count, 1));

        for (int step = 0; step < count; step++)
        {
            int i = (start + step) % count;
            if (popupPrefabs[i] != null)
                return i;
        }
        return -1;
    }

    private LevelUpFloater Get(int kind)
    {
        // 반환된 인스턴스 재사용(파괴된 건 건너뜀)
        while (free[kind].Count > 0)
        {
            LevelUpFloater f = free[kind].Dequeue();
            if (f != null)
                return f;
        }
        return Create(kind);
    }

    //자식으로 낳는다 — 캐릭터가 움직이면 팝업도 따라가고, 캐릭터가 사라질 때 같이 사라진다.
    private LevelUpFloater Create(int kind)
    {
        LevelUpFloater f = Instantiate(popupPrefabs[kind], transform, false);
        f.gameObject.SetActive(false);
        return f;
    }

    private void Return(int kind, LevelUpFloater f)
    {
        if (f != null)
            free[kind].Enqueue(f);
    }
}
