using System.Collections.Generic;
using UnityEngine;

public class UIPoolManager : MonoBehaviour
{
    public static UIPoolManager Instance;

    [Header("Pool Settings")]
    public UIFollowTarget uiPrefab;
    public Transform canvasTransform;
    public int initialPoolSize = 10;

    private Queue<UIFollowTarget> inactivePool = new Queue<UIFollowTarget>();
    private List<UIFollowTarget> activeList = new List<UIFollowTarget>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializePool();
    }

    private void InitializePool()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewUIObject();
        }
    }

    private UIFollowTarget CreateNewUIObject()
    {
        UIFollowTarget ui = Instantiate(uiPrefab, canvasTransform);
        ui.gameObject.SetActive(false);
        inactivePool.Enqueue(ui);
        return ui;
    }

    public UIFollowTarget SpawnUI(Transform target)
    {
        UIFollowTarget ui;

        if (inactivePool.Count > 0)
            ui = inactivePool.Dequeue();
        else
        {
            ui = CreateNewUIObject();
            ui = inactivePool.Dequeue();
        }

        ui.gameObject.SetActive(true);
        ui.SetTarget(target); // 여기서 애니메이션 코루틴이 시작됨
        activeList.Add(ui);

        return ui;
    }

    public void ReturnUI(UIFollowTarget ui)
    {
        // 이미 반환되었거나 리스트에 없다면 무시 (중복 반환 방지)
        if (ui == null || !activeList.Contains(ui)) return;

        activeList.Remove(ui);

        // UI 내부 초기화 (코루틴 정지 등)
        ui.ClearTarget();
        ui.gameObject.SetActive(false);

        inactivePool.Enqueue(ui);
    }
}