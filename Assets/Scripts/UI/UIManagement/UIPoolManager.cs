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

    private class Pool
    {
        public Queue<GameObject> objectQueue = new Queue<GameObject>();
        public GameObject prefab;
        public Transform parentTransform;
    }

    private Dictionary<UIType, Pool> poolDictionary = new Dictionary<UIType, Pool>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializePools();
    }

    private void InitializePools()
    {
        foreach (var poolDef in prewarmPools)
        {
            if (poolDef.prefab == null) continue;

            UIType type = poolDef.uiType;

            if (!poolDictionary.ContainsKey(type))
            {
                Transform parent = poolDef.parentTransform != null ? poolDef.parentTransform : canvasTransform;

                Pool newPool = new Pool
                {
                    prefab = poolDef.prefab,
                    parentTransform = parent
                };

                poolDictionary.Add(type, newPool);
            }

            for (int i = 0; i < poolDef.initialSize; i++)
            {
                CreateNewUIObject(type);
            }
        }
    }

    private GameObject CreateNewUIObject(UIType type)
    {
        Pool pool = poolDictionary[type];

        GameObject ui = Instantiate(pool.prefab, pool.parentTransform);
        ui.SetActive(false);

        PooledUI pooledUI = ui.AddComponent<PooledUI>();
        pooledUI.originType = type;

        pool.objectQueue.Enqueue(ui);
        return ui;
    }

    public GameObject SpawnUI(UIType type)
    {
        if (!poolDictionary.TryGetValue(type, out Pool targetPool))
        {
            Debug.LogError($"[UIPoolManager] {type}에 해당하는 풀이 없습니다!");
            return null;
        }

        GameObject ui;

        if (targetPool.objectQueue.Count > 0)
        {
            ui = targetPool.objectQueue.Dequeue();
        }
        else
        {
            ui = CreateNewUIObject(type);
            ui = targetPool.objectQueue.Dequeue();
        }

        ui.SetActive(true);
        return ui;
    }

    public void ReturnUI(GameObject ui)
    {
        if (ui == null) return;

        if (ui.TryGetComponent(out PooledUI pooledUI))
        {
            ui.SetActive(false);
            poolDictionary[pooledUI.originType].objectQueue.Enqueue(ui);
        }
        else
        {
            Debug.LogWarning("UIPoolManager에 등록되지 않은 객체 반환 시도: " + ui.name);
            Destroy(ui);
        }
    }

    public void DisableParent()
    {
        foreach (var pool in poolDictionary.Values)
        {
            if (pool.parentTransform != null)
            {
                pool.parentTransform.gameObject.SetActive(false);
            }
        }
    }
}