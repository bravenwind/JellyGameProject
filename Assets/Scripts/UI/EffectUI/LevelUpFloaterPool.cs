using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 성장 팝업 프리팹들을 풀링하는 컨테이너. 플레이어 프리팹의 자식(LevelUpFloaterPool)에 있다.
/// Play()를 부를 때마다 <b>등록된 프리팹 중 하나를 무작위로 골라</b> 그 종류의 풀에서 꺼내 재생한다.
///
/// ★ 종류별로 줄을 따로 세운다
///   프리팹이 여러 개인데 큐가 하나면, 반납된 "냠!"을 다음 "+1" 자리에 꺼내 쓰게 된다.
///   그래서 free[i]가 popupPrefabs[i]의 인스턴스만 담는다.
///
/// ★ 예전엔 프리팹 없이 코드가 GameObject를 만들었다
///   [SerializeField] floaterPrefab이 있긴 했는데 <b>항상 null</b>이었다 —
///   이 컴포넌트를 PlayerBridge.Awake가 런타임에 AddComponent로 만들었기 때문에
///   인스펙터에서 프리팹을 꽂을 자리 자체가 없었다. 그래서 "비우면 런타임 생성"이라는
///   폴백만 언제나 도는 구조였고, 팝업은 한 종류로 고정이었다.
///   지금은 플레이어 프리팹에 이 컨테이너가 들어 있어 프리팹 목록을 인스펙터에서 정한다.
/// </summary>
public class LevelUpFloaterPool : MonoBehaviour
{
    [Tooltip("띄울 팝업 프리팹들. 이 중 하나가 무작위로 뜬다. Prefabs/UI/Popups")]
    [SerializeField] private LevelUpFloater[] popupPrefabs;

    [Tooltip("종류마다 미리 만들어둘 개수")]
    [SerializeField] private int prewarmPerPrefab = 2;

    private Transform scaleRef;   // 크기 상쇄 기준 = 플레이어 루트(이 컨테이너의 부모)
    private Queue<LevelUpFloater>[] free;

    private void Awake()
    {
        scaleRef = transform.parent;

        if (popupPrefabs == null || popupPrefabs.Length == 0)
        {
            //조용히 안 뜨면 "왜 안 나오지"로 한참 헤맨다. 배선 사고는 소리를 낸다.
            Debug.LogError("[성장팝업] popupPrefabs가 비어 있습니다 — "
                + "플레이어 프리팹의 LevelUpFloaterPool에 팝업 프리팹을 넣어주세요.", this);
            enabled = false;
            return;
        }

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

    /// <summary>성장 이펙트 1회 표시. 동시에 여러 번 불려도 각 인스턴스가 독립적으로 뜬다.</summary>
    public void Play()
    {
        if (!enabled || free == null)
            return;

        if (scaleRef == null)
            scaleRef = transform.parent;

        int kind = PickKind();
        if (kind < 0)
            return;

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
        int start = Random.Range(0, count);

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

    //자식으로 낳는다 — 플레이어가 움직이면 팝업도 따라가고, 캐릭터가 사라질 때 같이 사라진다.
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
