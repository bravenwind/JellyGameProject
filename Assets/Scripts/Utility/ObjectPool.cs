using System.Collections.Generic;
using UnityEngine;

public class ObjectPool<T> where T : Component
{
    private readonly T _prefab;
    private readonly Transform _parent;
    private readonly Queue<T> _available = new Queue<T>();

    public ObjectPool(T prefab, Transform parent = null, int prewarmCount = 0)
    {
        _prefab = prefab;
        _parent = parent;
        for (int i = 0; i < prewarmCount; i++)
            _available.Enqueue(CreateInstance());
    }

    private T CreateInstance()
    {
        T instance = Object.Instantiate(_prefab, _parent);
        instance.gameObject.SetActive(false);
        return instance;
    }

    public T Get()
    {
        T instance = _available.Count > 0 ? _available.Dequeue() : CreateInstance();
        instance.gameObject.SetActive(true);
        return instance;
    }

    public void Return(T instance)
    {
        instance.gameObject.SetActive(false);
        _available.Enqueue(instance);
    }

    public void ReturnAll(List<T> activeList)
    {
        foreach (var item in activeList)
            Return(item);
        activeList.Clear();
    }
}
