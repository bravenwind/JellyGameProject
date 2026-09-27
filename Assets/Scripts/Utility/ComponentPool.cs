using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

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
