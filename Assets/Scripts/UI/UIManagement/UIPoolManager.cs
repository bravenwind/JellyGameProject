using System.Collections.Generic;
using UnityEngine;

public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager Instance;

    [Header("Settings")]
    public Transform canvasTransform;

    [System.Serializable]
    public struct PoolDefinition
    {
        public UIFollowTarget prefab;
        public int initialSize;
    }
    public List<PoolDefinition> prewarmPools;

    // 프리팹 ID별 오브젝트 대기열
    private Dictionary<int, Queue<UIFollowTarget>> poolDictionary = new Dictionary<int, Queue<UIFollowTarget>>();

    // 추가: 프리팹 ID별 부모 트랜스폼 관리
    private Dictionary<int, Transform> poolParentDictionary = new Dictionary<int, Transform>();

    // 활성화된 객체가 어떤 프리팹 출신인지 추적
    private Dictionary<int, int> activeObjectMap = new Dictionary<int, int>();

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

            // 풀과 부모 오브젝트 생성
            CreatePoolIfNeeded(poolDef.prefab);

            for (int i = 0; i < poolDef.initialSize; i++)
            {
                CreateNewUIObject(poolDef.prefab);
            }
        }
    }

    private void CreatePoolIfNeeded(UIFollowTarget prefab)
    {
        int prefabID = prefab.GetInstanceID();

        if (!poolDictionary.ContainsKey(prefabID))
        {
            poolDictionary.Add(prefabID, new Queue<UIFollowTarget>());

            // --- 부모 오브젝트 생성 로직 추가 ---
            GameObject poolParent = new GameObject($"Pool_{prefab.name}");
            poolParent.transform.SetParent(canvasTransform, false);

            // 캔버스에서 가장 뒤에 위치하게 함 (계층 구조 맨 위로 이동)
            poolParent.transform.SetAsFirstSibling();

            // UI 관리를 위해 RectTransform 설정 (필요시)
            RectTransform rt = poolParent.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            poolParentDictionary.Add(prefabID, poolParent.transform);
        }
    }

    private UIFollowTarget CreateNewUIObject(UIFollowTarget prefab)
    {
        int prefabID = prefab.GetInstanceID();
        Transform parent = poolParentDictionary[prefabID];

        // 해당 프리팹 전용 부모 밑에 생성
        UIFollowTarget ui = Instantiate(prefab, parent);
        ui.gameObject.SetActive(false);

        poolDictionary[prefabID].Enqueue(ui);
        return ui;
    }

    public UIFollowTarget SpawnUI(UIFollowTarget prefab, Transform target)
    {
        if (prefab == null) return null;

        int prefabID = prefab.GetInstanceID();
        CreatePoolIfNeeded(prefab);

        UIFollowTarget ui;
        Queue<UIFollowTarget> targetPool = poolDictionary[prefabID];

        if (targetPool.Count > 0)
        {
            ui = targetPool.Dequeue();
        }
        else
        {
            // 풀에 없으면 새로 생성 (이때 자동으로 해당 부모 밑에 들어감)
            ui = CreateNewUIObject(prefab);
            ui = targetPool.Dequeue();
        }

        ui.gameObject.SetActive(true);
        ui.SetTarget(target);

        int objID = ui.GetInstanceID();
        if (!activeObjectMap.ContainsKey(objID))
        {
            activeObjectMap.Add(objID, prefabID);
        }

        return ui;
    }

    public void ReturnUI(UIFollowTarget ui)
    {
        if (ui == null) return;

        int objID = ui.GetInstanceID();

        if (activeObjectMap.TryGetValue(objID, out int originPrefabID))
        {
            ui.ClearTarget();
            ui.gameObject.SetActive(false);

            if (poolDictionary.ContainsKey(originPrefabID))
            {
                poolDictionary[originPrefabID].Enqueue(ui);
            }

            activeObjectMap.Remove(objID);
        }
        else
        {
            Debug.LogWarning("UIPoolManager에 등록되지 않은 객체 반환: " + ui.name);
            Destroy(ui.gameObject);
        }
    }

    public void DisableParent()
    {
        // 딕셔너리에 저장된 모든 부모 Transform 값(Values)을 하나씩 꺼내옵니다.
        foreach (Transform parentTransform in poolParentDictionary.Values)
        {
            if (parentTransform != null)
            {
                parentTransform.gameObject.SetActive(false);
            }
        }
    }
}