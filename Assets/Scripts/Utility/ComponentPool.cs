using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

//프리팹을 복제해 재사용하는 컴포넌트 풀
//생성·해제 규칙은 UnityEngine.Pool.ObjectPool에 맡기고 프리팹 인스턴스화만 여기서 정한다
public class ComponentPool<T> where T : Component
{
    private const int MAX_SIZE = 256;

    private readonly T prefab;
    private readonly Transform parent;
    private readonly ObjectPool<T> pool;

    public ComponentPool(T prefab, Transform parent = null, int prewarmCount = 0)
    {
        this.prefab = prefab;
        this.parent = parent;

        pool = new ObjectPool<T>(
            createFunc: Create,
            actionOnGet: item => item.gameObject.SetActive(true),
            actionOnRelease: item => item.gameObject.SetActive(false),
            actionOnDestroy: item => Object.Destroy(item.gameObject),
            collectionCheck: true,
            defaultCapacity: Mathf.Max(1, prewarmCount),
            maxSize: MAX_SIZE);

        Prewarm(prewarmCount);
    }

    public T Get()
    {
        return pool.Get();
    }

    public void Return(T instance)
    {
        if (instance != null)
            pool.Release(instance);
    }

    public void ReturnAll(List<T> activeList)
    {
        foreach (T item in activeList)
            Return(item);

        activeList.Clear();
    }

    public void Clear()
    {
        pool.Clear();
    }

    // ★ 세 번째 인자 false(worldPositionStays)를 반드시 넘긴다
    //   Instantiate(원본, 부모)는 worldPositionStays가 true다. 그러면 유니티가
    //   "월드 스케일을 프리팹 그대로 유지"하려고 자식의 localScale에
    //   프리팹스케일 / 부모의 lossyScale 을 넣는다.
    //   여기 parent는 캐릭터 루트(성장하면 4배까지 커진다)나 UI 컨테이너(캔버스가
    //   스케일을 매 프레임 다시 쓴다)라, 그때그때 부모 크기에 따라 인스턴스 크기가
    //   달라진다. 같은 실수가 OffScreenPlayerIndicator에서 "플레이어마다 삼각형
    //   크기가 다르다"로 나타났다.
    //   false를 주면 프리팹의 localScale이 그대로 들어와 생성 시점과 무관해진다.
    private T Create()
    {
        T instance = Object.Instantiate(prefab, parent, false);
        instance.gameObject.SetActive(false);
        return instance;
    }

    private void Prewarm(int count)
    {
        if (count <= 0)
            return;

        List<T> warmed = new(count);

        for (int i = 0; i < count; i++)
            warmed.Add(pool.Get());

        foreach (T item in warmed)
            pool.Release(item);
    }
}
