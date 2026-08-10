using System.Collections.Generic;
using UnityEngine;

public enum UIType { JellyEat, ScaleIncrease, MilkScaleDecrease }

public class PooledUI : MonoBehaviour
{
    public UIType originType;
}

public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager Instance;

    [Header("Settings")]
    public Transform canvasTransform;

    [System.Serializable]
    public struct PoolDefinition
    {
        public UIType uiType;
        public GameObject prefab;
        public Transform parentTransform;
        public int initialSize;
    }

    public List<PoolDefinition> prewarmPools;

    private Dictionary<UIType, ComponentPool<Transform>> _pools = new Dictionary<UIType, ComponentPool<Transform>>();
    private Dictionary<UIType, Transform> _parents = new Dictionary<UIType, Transform>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializePools();
    }

    private void InitializePools()
    {
        foreach (var def in prewarmPools)
        {
            if (def.prefab == null || _pools.ContainsKey(def.uiType)) continue;

            Transform parent = def.parentTransform != null ? def.parentTransform : canvasTransform;
            _pools[def.uiType] = new ComponentPool<Transform>(def.prefab.transform, parent, def.initialSize);
            _parents[def.uiType] = parent;
        }
    }

    public GameObject SpawnUI(UIType type)
    {
        if (!_pools.TryGetValue(type, out var pool))
        {
            Debug.LogError($"[UIPoolManager] {type}에 해당하는 풀이 없습니다!");
            return null;
        }

        Transform t = pool.Get();
        if (!t.TryGetComponent(out PooledUI tag))
        {
            tag = t.gameObject.AddComponent<PooledUI>();
            tag.originType = type;
        }
        return t.gameObject;
    }

    public void ReturnUI(GameObject ui)
    {
        if (ui == null) return;

        if (ui.TryGetComponent(out PooledUI tag) && _pools.TryGetValue(tag.originType, out var pool))
        {
            pool.Return(ui.transform);
        }
        else
        {
            Debug.LogWarning("UIPoolManager에 등록되지 않은 객체 반환 시도: " + ui.name);
            Destroy(ui);
        }
    }

    public void DisableParent()
    {
        foreach (var parent in _parents.Values)
        {
            if (parent != null)
                parent.gameObject.SetActive(false);
        }
    }
}