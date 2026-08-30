using System.Collections.Generic;
using UnityEngine;
using System;

/// <summary>
/// 화면에 잠깐 떴다 사라지는 UI의 종류.
///
/// ★ 값을 명시적으로 박아둔다 — 절대 다시 매기지 말 것
///   이 enum은 UIPoolManager.PoolDefinition.uiType으로 <b>씬에 정수로 직렬화된다.</b>
///   값을 안 적어두면 가운데 항목 하나를 지우는 순간 뒤가 전부 한 칸씩 밀려서,
///   씬에 저장된 uiType이 <b>엉뚱한 풀</b>을 가리킨다. 컴파일은 통과하고 런타임에
///   조용히 다른 UI가 뜨거나 안 뜨므로 원인을 찾기가 매우 어렵다.
///   (SceneNetId 때 프리팹 오버라이드 244개가 고아가 됐던 것과 같은 종류의 사고다)
///
///   1·2번이 비어 있다. 아무도 스폰하지 않던 ScaleIncrease(1)와,
///   축소 경로가 사라지며 죽은 MilkScaleDecrease(2)를 뺀 자리다.
///   새 항목은 3부터 붙이고, 빈 번호를 재사용하지 말 것.
/// </summary>
public enum UIType
{
    JellyEat = 0,
}

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