using System.Collections.Generic;
using UnityEngine;

public class UIPoolManager2 : MonoBehaviour
{
    public static UIPoolManager2 Instance;

    [Header("Pool Settings")]
    public UIFollowTarget2 uiPrefab;
    public Transform canvasTransform;
    public int initialPoolSize = 10;

    private ObjectPool<UIFollowTarget2> _pool;
    private List<UIFollowTarget2> _activeList = new List<UIFollowTarget2>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        _pool = new ObjectPool<UIFollowTarget2>(uiPrefab, canvasTransform, initialPoolSize);
    }

    public UIFollowTarget2 SpawnUI(Transform target)
    {
        UIFollowTarget2 ui = _pool.Get();
        ui.SetTarget(target);
        _activeList.Add(ui);
        return ui;
    }

    public void ReturnUI(UIFollowTarget2 ui)
    {
        if (ui == null || !_activeList.Contains(ui)) return;

        _activeList.Remove(ui);
        ui.ClearTarget();
        _pool.Return(ui);
    }
}