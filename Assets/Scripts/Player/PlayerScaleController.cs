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

    public float currentScaleValue { get; private set; } = 1f;

    private Queue<IEnumerator> scaleQueue = new Queue<IEnumerator>();
    private bool isScaling = false;
    public bool IsScaling => isScaling;

    public bool isBot = false;

    private void Start()
    {
        originalScale = Vector3.one;
        currentScale = transform.localScale;
        currentScaleValue = transform.localScale.x;
        if (!isBot)
        {
            DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius;
            DataManager.Instance.playerCurrentScale = currentScaleValue;
            PlayerEvents.OnScaleUIUpdate?.Invoke();
        }
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

    private bool CrossedThresholdUp(float prevScale, float newScale)
    {
        float first = DataManager.Instance.cameraZoomFirstThreshold;
        float step  = DataManager.Instance.cameraZoomThresholdStep;
        if (step <= 0f || newScale <= prevScale) return false;
        int n = Mathf.CeilToInt((prevScale - first) / step);
        float nextThreshold = first + n * step;
        if (nextThreshold <= prevScale) nextThreshold += step;
        return nextThreshold < newScale;
    }

    private bool CrossedThresholdDown(float prevScale, float newScale)
    {
        float first = DataManager.Instance.cameraZoomFirstThreshold;
        float step  = DataManager.Instance.cameraZoomThresholdStep;
        if (step <= 0f || newScale >= prevScale) return false;
        int n = Mathf.FloorToInt((prevScale - first) / step);
        float prevThreshold = first + n * step;
        if (prevThreshold >= prevScale) prevThreshold -= step;
        return prevThreshold > newScale;
    }

    private IEnumerator ScaleTo(float targetValue, float duration, bool growing, bool playEffect = false)
    {
        targetValue = Mathf.Clamp(targetValue, DataManager.Instance.minScale, DataManager.Instance.maxScale);
        if (Mathf.Approximately(targetValue, currentScaleValue)) yield break;

        float prevScale = currentScaleValue;
        bool hitsThresholdUp   = growing  && CrossedThresholdUp(prevScale, targetValue);
        bool hitsThresholdDown = !growing && CrossedThresholdDown(prevScale, targetValue);

        if (softBody3D != null) softBody3D.DisableCloth();

        if (!isBot)
        {
            if (growing && playEffect)
            {
                if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayScaleUpSound();
                UIPoolManager.Instance?.SpawnUI(UIType.ScaleIncrease);
            }
            if (hitsThresholdUp)
                PlayerEvents.OnCameraScaleIncreased?.Invoke();
            if (!growing)
                UIPoolManager.Instance?.SpawnUI(UIType.MilkScaleDecrease);
            if (hitsThresholdDown)
                PlayerEvents.OnCameraScaleDecreased?.Invoke();
        }

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

        if (!isBot && playerController != null)
        {
            playerController.jumpForce = currentScaleValue >= DataManager.Instance.jumpScaleThreshold
                ? playerController.originalJumpForce + DataManager.Instance.IncreaseJumpForceValue
                : playerController.originalJumpForce;
            DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius
                + (currentScaleValue - 1f) * DataManager.Instance.detectPlusRadiusPerLevel;
            DataManager.Instance.playerCurrentScale = currentScaleValue;
        }

        if (softBody3D != null) StartCoroutine(softBody3D.EnableAndRebuildCloth());

        if (!isBot) PlayerEvents.OnScaleUIUpdate?.Invoke();

        // NavMeshAgent 재정렬 (봇)
        AIPlayerMovement aiMovement = GetComponent<AIPlayerMovement>();
        if (aiMovement != null) aiMovement.RecenterCC();
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
        if (!isBot)
        {
            PlayerEvents.OnCameraOrthoSizeChanged?.Invoke(6.1f);
            DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius;
        }

        StopAllCoroutines();
        scaleQueue.Clear();
        isScaling = false;
        currentScaleValue = 1f;
        currentScale = originalScale;
        transform.localScale = originalScale;

        if (!isBot)
        {
            DataManager.Instance.playerCurrentScale = 1f;
            PlayerEvents.OnScaleUIUpdate?.Invoke();
        }
    }
}
