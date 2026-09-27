using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerScaleController : MonoBehaviour
{
    [SerializeField] private SoftBody3D softBody3D;

    private Vector3 currentScale;

    public float CurrentScaleValue { get; private set; }

    public float PendingScale { get; private set; }

    private Queue<IEnumerator> scaleQueue = new Queue<IEnumerator>();
    private bool isScaling = false;

    private Coroutine jellyBatchCoroutine;

    public event Action<float> OnScaleSettled;

    public event Action<bool> OnScaleGrowStarted;

    public event Action OnScaleCrossedThreshold;

    public event Action OnScalePhysicsRebuilt;

    private void Awake()
    {
        currentScale = transform.localScale;
        CurrentScaleValue = currentScale.x;
        PendingScale = CurrentScaleValue;
    }

    private void Start()
    {
        OnScaleSettled?.Invoke(CurrentScaleValue);
    }

    public void GrowByJelly()
    {
        PendingScale += DataManager.Instance.JellyScaleIncrease;

        if (jellyBatchCoroutine == null)
            jellyBatchCoroutine = StartCoroutine(BatchedJellyGrow());
    }

    private IEnumerator BatchedJellyGrow()
    {
        yield return null;
        jellyBatchCoroutine = null;
        QueueScaleChange(ScaleTo(PendingScale, DataManager.Instance.GrowAnimTime, playEffect: false));
    }

    public void GrowByAbsorbing(float absorbedScaleValue)
    {
        PendingScale += absorbedScaleValue * DataManager.Instance.AbsorbScalePercent;
        QueueScaleChange(ScaleTo(PendingScale, DataManager.Instance.GrowAnimTime, playEffect: true));
    }

    public void GrowByBatHit(float growth)
    {
        PendingScale += growth;
        QueueScaleChange(ScaleTo(PendingScale, 0.3f, playEffect: true));
    }

    // 첫 기준보다 작으면 전부 -1
    private int GetScaleTier(float scale)
    {
        float first = DataManager.Instance.CameraZoomFirstThreshold;
        float step = DataManager.Instance.CameraZoomThresholdStep;

        if (step <= 0f)
            return 0;
        if (scale < first)
            return -1;

        return Mathf.FloorToInt((scale - first) / step);
    }

    private bool CrossedThresholdUp(float prevScale, float newScale)
    {
        if (newScale <= prevScale)
            return false;
        return GetScaleTier(newScale) > GetScaleTier(prevScale);
    }

    private IEnumerator ScaleTo(float targetValue, float duration, bool playEffect = true)
    {
        if (Mathf.Approximately(targetValue, CurrentScaleValue))
            yield break;

        float prevScale = CurrentScaleValue;
        bool hitsThresholdUp = CrossedThresholdUp(prevScale, targetValue);

        if (softBody3D != null)
            softBody3D.DisableCloth();

        OnScaleGrowStarted?.Invoke(playEffect);

        if (hitsThresholdUp)
            OnScaleCrossedThreshold?.Invoke();

        Vector3 startScale = currentScale;
        Vector3 targetScale = Vector3.one * targetValue;
        CurrentScaleValue = targetValue;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            currentScale = Vector3.Lerp(startScale, targetScale, t / duration);
            transform.localScale = currentScale;
            yield return null;
        }
        transform.localScale = currentScale = targetScale;

        OnScaleSettled?.Invoke(CurrentScaleValue);

        if (softBody3D != null)
            softBody3D.RequestRebuildCloth();

        OnScalePhysicsRebuilt?.Invoke();
    }

    public void QueueScaleChange(IEnumerator scaleRoutine)
    {
        scaleQueue.Enqueue(scaleRoutine);
        if (!isScaling)
            StartCoroutine(ProcessScaleQueue());
    }

    private IEnumerator ProcessScaleQueue()
    {
        isScaling = true;
        while (scaleQueue.Count > 0)
        {
            yield return StartCoroutine(scaleQueue.Dequeue());
        }
        isScaling = false;
    }
}
