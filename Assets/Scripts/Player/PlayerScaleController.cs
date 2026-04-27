using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerScaleController : MonoBehaviour
{
    [Header("References")]
    public PlayerController playerController;
    public SoftBody3D softBody3D;
    public Rigidbody[] rigidbodies;

    [Header("Scale Settings")]
    public Vector3 originalScale;
    private Vector3 currentScale;

    public float currentScaleValue { get; private set; } = 2f;

    private Queue<IEnumerator> scaleQueue = new Queue<IEnumerator>();
    private bool isScaling = false;
    public bool IsScaling => isScaling;

    private IEntityBridge _bridge;

    private void Awake()
    {
        _bridge = GetComponentInParent<IEntityBridge>();
    }

    private void Start()
    {
        originalScale = Vector3.one;
        currentScale = transform.localScale;
        currentScaleValue = transform.localScale.x;
        _bridge?.OnScaleInit(currentScaleValue);
    }

    public void GrowByJelly()
    {
        float target = Mathf.Min(currentScaleValue + DataManager.Instance.jellyScaleIncrease, DataManager.Instance.maxScale);
        QueueScaleChange(ScaleTo(target, DataManager.Instance.scaleIncreaseTime, growing: true, playEffect: false));
    }

    public void GrowByAbsorbing(float absorbedScaleValue)
    {
        float gain = absorbedScaleValue * DataManager.Instance.absorbScalePercent;
        float target = Mathf.Min(currentScaleValue + gain, DataManager.Instance.maxScale);
        QueueScaleChange(ScaleTo(target, DataManager.Instance.scaleIncreaseTime, growing: true, playEffect: true));
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

    private IEnumerator ScaleTo(float targetValue, float duration, bool growing, bool playEffect = false)
    {
        targetValue = Mathf.Clamp(targetValue, DataManager.Instance.minScale, DataManager.Instance.maxScale);
        if (Mathf.Approximately(targetValue, currentScaleValue)) yield break;

        float prevScale = currentScaleValue;
        bool hitsThresholdUp   = growing  && CrossedThresholdUp(prevScale, targetValue);
        bool hitsThresholdDown = !growing && CrossedThresholdDown(prevScale, targetValue);

        if (softBody3D != null) softBody3D.DisableCloth();

        if (growing) _bridge?.OnGrowEffect(playEffect);
        if (hitsThresholdUp) _bridge?.OnScaleThresholdUp();
        if (!growing) _bridge?.OnShrinkEffect();
        if (hitsThresholdDown) _bridge?.OnScaleThresholdDown();

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

        _bridge?.OnScaleCompleted(currentScaleValue, playerController);

        if (softBody3D != null) StartCoroutine(softBody3D.EnableAndRebuildCloth());

        _bridge?.OnScaleUIUpdate();
        _bridge?.OnPostScalePhysics();
    }

    public IEnumerator DecreaseScale(float decreaseTime)
    {
        float target = Mathf.Max(currentScaleValue - DataManager.Instance.scaleDecreaseAmount, DataManager.Instance.minScale);
        yield return ScaleTo(target, decreaseTime, growing: false);
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
        _bridge?.OnScaleReset();

        StopAllCoroutines();
        scaleQueue.Clear();
        isScaling = false;
        currentScaleValue = 1f;
        currentScale = originalScale;
        transform.localScale = originalScale;
    }
}
