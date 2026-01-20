using System.Collections.Generic;
using UnityEngine;

public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager Instance;

    [Header("Settings")]
    public Transform canvasTransform;

    // 인스펙터에서 이펙트별로 미리 풀을 생성하고 싶을 때 정의
    [System.Serializable]
    public struct PoolDefinition
    {
        public UIFollowTarget prefab;
        public int initialSize;
    }
    public List<PoolDefinition> prewarmPools;

    // Key: Prefab의 InstanceID, Value: 해당 Prefab의 대기열(Queue)
    private Dictionary<int, Queue<UIFollowTarget>> poolDictionary = new Dictionary<int, Queue<UIFollowTarget>>();

    // Key: 생성된 객체의 InstanceID, Value: 원본 Prefab의 InstanceID (반환할 곳을 찾기 위함)
    private Dictionary<int, int> activeObjectMap = new Dictionary<int, int>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializePools();
    }

    private void InitializePools()
    {
        // 인스펙터에 등록된 풀 미리 생성
        foreach (var poolDef in prewarmPools)
        {
            if (poolDef.prefab == null) continue;
            CreatePoolIfNeeded(poolDef.prefab);

            for (int i = 0; i < poolDef.initialSize; i++)
            {
                CreateNewUIObject(poolDef.prefab);
            }
        }
    }

    // 풀(Queue)이 없으면 새로 생성하는 메서드
    private void CreatePoolIfNeeded(UIFollowTarget prefab)
    {
        int prefabID = prefab.GetInstanceID();
        if (!poolDictionary.ContainsKey(prefabID))
        {
            poolDictionary.Add(prefabID, new Queue<UIFollowTarget>());
        }
    }

    private UIFollowTarget CreateNewUIObject(UIFollowTarget prefab)
    {
        int prefabID = prefab.GetInstanceID();

        UIFollowTarget ui = Instantiate(prefab, canvasTransform);
        ui.gameObject.SetActive(false);

        // 생성된 객체를 해당 프리팹 ID의 큐에 넣음
        poolDictionary[prefabID].Enqueue(ui);

        return ui;
    }

    // 변경점: 어떤 Prefab을 스폰할지 매개변수로 받음
    public UIFollowTarget SpawnUI(UIFollowTarget prefab, Transform target)
    {
        if (prefab == null)
        {
            Debug.LogError("SpawnUI: Prefab is null!");
            return null;
        }

        int prefabID = prefab.GetInstanceID();
        CreatePoolIfNeeded(prefab); // 풀이 없으면 즉석에서 생성 (Lazy Init)

        UIFollowTarget ui;
        Queue<UIFollowTarget> targetPool = poolDictionary[prefabID];

        if (targetPool.Count > 0)
        {
            ui = targetPool.Dequeue();
        }
        else
        {
            ui = CreateNewUIObject(prefab);
            ui = targetPool.Dequeue();
        }

        ui.gameObject.SetActive(true);
        ui.SetTarget(target);

        // 활성화된 객체 추적 (반환 시 어떤 풀로 갈지 알기 위해)
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

        // 이 객체가 우리 풀에서 관리되는 객체인지 확인
        if (activeObjectMap.TryGetValue(objID, out int originPrefabID))
        {
            // UI 초기화
            ui.ClearTarget();
            ui.gameObject.SetActive(false);

            // 원래 있던(원본 프리팹 ID에 해당하는) 큐로 반환
            if (poolDictionary.ContainsKey(originPrefabID))
            {
                poolDictionary[originPrefabID].Enqueue(ui);
            }

            // 활성 목록에서 제거
            activeObjectMap.Remove(objID);
        }
        else
        {
            Debug.LogWarning("UIPoolManager에 등록되지 않은 객체를 반환하려고 했습니다: " + ui.name);
            Destroy(ui.gameObject); // 관리 대상 아니면 그냥 파괴
        }
    }
}