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
    [Header("팝업 프리팹 (LevelUpFloater 부착). 비우면 런타임 생성")]
    public LevelUpFloater floaterPrefab;

    [Tooltip("미리 생성해둘 인스턴스 개수")]
    public int prewarm = 3;

    private Transform _scaleRef;   // 크기 상쇄 기준 = 플레이어 루트(이 컨테이너의 부모)
    private readonly Queue<LevelUpFloater> _free = new Queue<LevelUpFloater>();

    private void Awake()
    {
        _scaleRef = transform.parent;
        for (int i = 0; i < prewarm; i++)
            _free.Enqueue(Create());
    }

    /// <summary>성장 이펙트 1회 표시. 동시 호출되면 각 인스턴스가 독립적으로 떠오른다.</summary>
    public void Play()
    {
        if (_scaleRef == null) _scaleRef = transform.parent;

        LevelUpFloater f = Get();
        if (f == null) return;
        f.Play(_scaleRef, Return);
    }

    private LevelUpFloater Get()
    {
        // 반환된 인스턴스 재사용(파괴된 건 건너뜀)
        while (_free.Count > 0)
        {
            LevelUpFloater f = _free.Dequeue();
            if (f != null) return f;
        }
        return Create();
    }

    private LevelUpFloater Create()
    {
        LevelUpFloater f;
        if (floaterPrefab != null)
        {
            f = Instantiate(floaterPrefab, transform);
        }
        else
        {
            // 프리팹 미지정 시 런타임 생성(폰트 에셋 의존 없이 동작)
            var go = new GameObject("LevelUpFloater");
            go.transform.SetParent(transform, false);
            f = go.AddComponent<LevelUpFloater>();
        }
        f.gameObject.SetActive(false);
        return f;
    }

    private void Return(LevelUpFloater f)
    {
        if (f != null) _free.Enqueue(f);
    }
}
