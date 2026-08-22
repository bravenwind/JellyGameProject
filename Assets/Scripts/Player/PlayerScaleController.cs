using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerScaleController : MonoBehaviour
{
    [Header("References")]
    public SoftBody3D softBody3D;

    [Header("Scale Settings")]
    public Vector3 originalScale;
    private Vector3 currentScale;

    public float currentScaleValue { get; private set; } = 2f;

    private float _pendingScale = 2f;
    public float PendingScale => _pendingScale;

    private Queue<IEnumerator> scaleQueue = new Queue<IEnumerator>();
    private bool isScaling = false;

    private Coroutine _jellyBatchCoroutine;

    // ── Scale Lifecycle Events ──
    public event Action<float> OnScaleInit;
    public event Action<float> OnScaleValueChanged;
    public event Action<float> OnScaleCompleted;
    public event Action OnScaleReset;
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
        _pendingScale = currentScaleValue;
        OnScaleInit?.Invoke(currentScaleValue);
    }

    public void GrowByJelly()
    {
        _pendingScale = Mathf.Min(_pendingScale + DataManager.Instance.jellyScaleIncrease, DataManager.Instance.maxScale);
        if (_jellyBatchCoroutine == null)
            _jellyBatchCoroutine = StartCoroutine(BatchedJellyGrow());
    }

    private IEnumerator BatchedJellyGrow()
    {
        yield return null;
        _jellyBatchCoroutine = null;
        QueueScaleChange(ScaleTo(_pendingScale, DataManager.Instance.scaleIncreaseTime, growing: true, playEffect: false));
    }

    public void GrowByAbsorbing(float absorbedScaleValue)
    {
        float gain = absorbedScaleValue * DataManager.Instance.absorbScalePercent;
        _pendingScale = Mathf.Min(_pendingScale + gain, DataManager.Instance.maxScale);
        QueueScaleChange(ScaleTo(_pendingScale, DataManager.Instance.scaleIncreaseTime, growing: true, playEffect: true));
    }

    public void GrowByBatHit(float growth)
    {
        _pendingScale = Mathf.Min(_pendingScale + growth, DataManager.Instance.maxScale);
        QueueScaleChange(ScaleTo(_pendingScale, 0.3f, growing: true, playEffect: true));
    }

    private int GetScaleTier(float scale)
    {
        float first = DataManager.Instance.cameraZoomFirstThreshold;
        float step = DataManager.Instance.cameraZoomThresholdStep;

        if (step <= 0f) return 0;
        if (scale < first) return -1;

        return Mathf.FloorToInt((scale - first) / step);
    }

    private bool CrossedThresholdUp(float prevScale, float newScale)
    {
        if (newScale <= prevScale) return false;
        return GetScaleTier(newScale) > GetScaleTier(prevScale);
    }

    private bool CrossedThresholdDown(float prevScale, float newScale)
    {
        if (newScale >= prevScale) return false;
        return GetScaleTier(newScale) < GetScaleTier(prevScale);
    }

    private IEnumerator ScaleTo(float targetValue, float duration, bool growing, bool playEffect = true)
    {
        targetValue = Mathf.Clamp(targetValue, DataManager.Instance.minScale, DataManager.Instance.maxScale);
        if (Mathf.Approximately(targetValue, currentScaleValue)) yield break;

        float prevScale = currentScaleValue;
        bool hitsThresholdUp   = growing  && CrossedThresholdUp(prevScale, targetValue);
        bool hitsThresholdDown = !growing && CrossedThresholdDown(prevScale, targetValue);

        if (softBody3D != null) softBody3D.DisableCloth();

        if (growing) OnGrowStarted?.Invoke(playEffect);
        if (hitsThresholdUp) OnScaleThresholdUp?.Invoke();
        if (!growing) OnShrinkStarted?.Invoke();
        if (hitsThresholdDown) OnScaleThresholdDown?.Invoke();

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

        if (softBody3D != null) softBody3D.RequestRebuildCloth();

        OnPostScalePhysics?.Invoke();
    }

    public void QueueScaleChange(IEnumerator scaleRoutine)
    {
        scaleQueue.Enqueue(scaleRoutine);
        if (!isScaling) StartCoroutine(ProcessScaleQueue());
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

    public void ResetScale()
    {
        OnScaleReset?.Invoke();

        StopAllCoroutines();
        // StopAllCoroutines는 BatchedJellyGrow를 첫 yield 대기 중에 끊어버릴 수 있는데,
        // 그러면 코루틴 본문이 _jellyBatchCoroutine을 null로 비우는 줄에 도달하지 못한다.
        // 핸들이 죽은 채 남으면 이후 GrowByJelly의 (== null) 가드가 영영 false가 되어
        // 젤리 성장이 다시 시작되지 않으므로, 여기서 직접 비워준다.
        _jellyBatchCoroutine = null;
        scaleQueue.Clear();
        isScaling = false;
        currentScaleValue = 1f;
        _pendingScale = 1f;
        currentScale = originalScale;
        transform.localScale = originalScale;
    }
}
