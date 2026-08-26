using System.Collections.Generic;
using UnityEngine;
using System;

public enum UIType { JellyEat, ScaleIncrease, MilkScaleDecrease }

public class PooledUI : MonoBehaviour
{
    //어느 풀에서 나왔는지. UIPoolManager가 꺼낼 때 찍어준다
    public UIType OriginType { get; set; }
}

public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager Instance;

    [Header("Settings")]
    [SerializeField] private Transform canvasTransform;

    [Serializable]
    public struct PoolDefinition
    {
        public UIType uiType;
        public GameObject prefab;
        public Transform parentTransform;
        public int initialSize;
    }

    [SerializeField] private List<PoolDefinition> prewarmPools;

    private Dictionary<UIType, ComponentPool<Transform>> pools = new Dictionary<UIType, ComponentPool<Transform>>();
    private Dictionary<UIType, Transform> parents = new Dictionary<UIType, Transform>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else Destroy(gameObject);

        InitializePools();
    }

    private void InitializePools()
    {
        foreach (var def in prewarmPools)
        {
            if (def.prefab == null || pools.ContainsKey(def.uiType))
                continue;

            Transform parent = def.parentTransform != null ? def.parentTransform : canvasTransform;
            pools[def.uiType] = new ComponentPool<Transform>(def.prefab.transform, parent, def.initialSize);
            parents[def.uiType] = parent;
        }
    }

    public GameObject SpawnUI(UIType type)
    {
        if (!pools.TryGetValue(type, out var pool))
        {
            Debug.LogError($"[UIPoolManager] {type}에 해당하는 풀이 없습니다!");
            return null;
        }

        Transform t = pool.Get();
        if (!t.TryGetComponent(out PooledUI tag))
        {
            tag = t.gameObject.AddComponent<PooledUI>();
            tag.OriginType = type;
        }
        return t.gameObject;
    }

}