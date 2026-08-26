using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerScaleController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SoftBody3D softBody3D;

    [Header("Scale Settings")]
    [SerializeField] private Vector3 originalScale;
    private Vector3 currentScale;

    public float currentScaleValue { get; private set; } = 2f;

    private float pendingScale = 2f;
    public float PendingScale => pendingScale;

    private Queue<IEnumerator> scaleQueue = new Queue<IEnumerator>();
    private bool isScaling = false;

    private Coroutine jellyBatchCoroutine;

    // ── Scale Lifecycle Events ──
    public event Action<float> OnScaleInit;
    public event Action<float> OnScaleValueChanged;
    public event Action<float> OnScaleCompleted;
    public event Action<bool> OnGrowStarted;
    public event Action OnShrinkStarted;
    public event Action OnScaleThresholdUp;
    public event Action OnScaleThresholdDown;
    public event Action OnPostScalePhysics;

    private void Start()
    {
        originalScale = Vector3.one;
        currentScale = transform.localScale;
        currentScaleValue = transform.localScale.x;
        pendingScale = currentScaleValue;
        OnScaleInit?.Invoke(currentScaleValue);
    }

    public void GrowByJelly()
    {
        pendingScale = Mathf.Min(pendingScale + DataManager.Instance.JellyScaleIncrease, DataManager.Instance.MaxScale);
        if (jellyBatchCoroutine == null)
            jellyBatchCoroutine = StartCoroutine(BatchedJellyGrow());
    }

    private IEnumerator BatchedJellyGrow()
    {
        yield return null;
        jellyBatchCoroutine = null;
        QueueScaleChange(ScaleTo(pendingScale, DataManager.Instance.ScaleIncreaseTime, growing: true, playEffect: false));
    }

    public void GrowByAbsorbing(float absorbedScaleValue)
    {
        float gain = absorbedScaleValue * DataManager.Instance.AbsorbScalePercent;
        pendingScale = Mathf.Min(pendingScale + gain, DataManager.Instance.MaxScale);
        QueueScaleChange(ScaleTo(pendingScale, DataManager.Instance.ScaleIncreaseTime, growing: true, playEffect: true));
    }

    public void GrowByBatHit(float growth)
    {
        pendingScale = Mathf.Min(pendingScale + growth, DataManager.Instance.MaxScale);
        QueueScaleChange(ScaleTo(pendingScale, 0.3f, growing: true, playEffect: true));
    }

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

    private bool CrossedThresholdDown(float prevScale, float newScale)
    {
        if (newScale >= prevScale)
            return false;
        return GetScaleTier(newScale) < GetScaleTier(prevScale);
    }

    private IEnumerator ScaleTo(float targetValue, float duration, bool growing, bool playEffect = true)
    {
        targetValue = Mathf.Clamp(targetValue, DataManager.Instance.MinScale, DataManager.Instance.MaxScale);
        if (Mathf.Approximately(targetValue, currentScaleValue))
            yield break;

        float prevScale = currentScaleValue;
        bool hitsThresholdUp   = growing  && CrossedThresholdUp(prevScale, targetValue);
        bool hitsThresholdDown = !growing && CrossedThresholdDown(prevScale, targetValue);

        if (softBody3D != null)
            softBody3D.DisableCloth();

        if (growing)
            OnGrowStarted?.Invoke(playEffect);
        if (hitsThresholdUp)
            OnScaleThresholdUp?.Invoke();
        if (!growing)
            OnShrinkStarted?.Invoke();
        if (hitsThresholdDown)
            OnScaleThresholdDown?.Invoke();

        Vector3 startScale = currentScale;
        Vector3 targetScale = originalScale * targetValue;
        currentScaleValue = targetValue;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            currentScale = Vector3.Lerp(startScale, targetScale, t / duration);
            transform.localScale = currentScale;
            yield return null;
        }
        transform.localScale = currentScale = targetScale;

        OnScaleValueChanged?.Invoke(currentScaleValue);
        OnScaleCompleted?.Invoke(currentScaleValue);

        if (softBody3D != null)
            softBody3D.RequestRebuildCloth();

        OnPostScalePhysics?.Invoke();
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
