using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// LevelUpFloater 팝업 프리팹을 풀링하는 컨테이너. 플레이어 루트의 자식으로 둔다.
/// Play() 호출마다 비어있는 인스턴스를 꺼내(없으면 생성) 재생한다.
/// 한 번에 여러 번 흡수해 동시에 여러 번 떠야 할 때는 각 인스턴스가 자식으로
/// 독립 재생되어 겹치지 않고 여러 개가 떠오른다.
/// </summary>
public class LevelUpFloaterPool : MonoBehaviour
{
    // ★ 프리팹 필드를 없앴다 — 배선할 방법이 없었다
    //   이 컴포넌트는 PlayerBridge.Awake가 <b>런타임에 AddComponent로</b> 만든다.
    //   플레이어 프리팹에 붙어 있지 않으니 인스펙터에서 프리팹을 꽂을 자리가 없고,
    //   따라서 [SerializeField] floaterPrefab은 <b>항상 null</b>이었다.
    //   "비우면 런타임 생성"이라는 폴백만 언제나 도는 구조였던 셈이다.
    //
    //   같은 이유로 공용 ComponentPool<T>를 쓰지 못한다 — 그건 복제할 프리팹을 받는다.
    //   프리팹 에셋을 만들어 플레이어 프리팹에 붙이면 그때 ComponentPool로 갈아탈 수 있다.
    [Tooltip("미리 생성해둘 인스턴스 개수")]
    [SerializeField] private int prewarm = 3;

    private Transform scaleRef;   // 크기 상쇄 기준 = 플레이어 루트(이 컨테이너의 부모)
    private readonly Queue<LevelUpFloater> free = new Queue<LevelUpFloater>();

    private void Awake()
    {
        scaleRef = transform.parent;
        for (int i = 0; i < prewarm; i++)
            free.Enqueue(Create());
    }

    /// <summary>성장 이펙트 1회 표시. 동시 호출되면 각 인스턴스가 독립적으로 떠오른다.</summary>
    public void Play()
    {
        if (scaleRef == null)
            scaleRef = transform.parent;

        LevelUpFloater f = Get();
        if (f == null)
            return;
        f.Play(scaleRef, Return);
    }

    private LevelUpFloater Get()
    {
        // 반환된 인스턴스 재사용(파괴된 건 건너뜀)
        while (free.Count > 0)
        {
            LevelUpFloater f = free.Dequeue();
            if (f != null)
                return f;
        }
        return Create();
    }

    private LevelUpFloater Create()
    {
        //폰트 에셋 의존 없이 런타임에 만든다. LevelUpFloater가 자기 표시를 스스로 꾸민다
        var go = new GameObject("LevelUpFloater");
        go.transform.SetParent(transform, false);

        LevelUpFloater f = go.AddComponent<LevelUpFloater>();
        f.gameObject.SetActive(false);
        return f;
    }

    private void Return(LevelUpFloater f)
    {
        if (f != null)
            free.Enqueue(f);
    }
}
