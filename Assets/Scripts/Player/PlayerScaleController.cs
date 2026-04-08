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

    private Queue<IEnumerator> scaleQueue = new Queue<IEnumerator>();
    private bool isScaling = false;
    public bool IsScaling => isScaling;

    public bool isBot = false;
    private int _botScaleLevel = 1;
    public int BotScaleLevel => _botScaleLevel;

    private void Start()
    {
        originalScale = Vector3.one;
        currentScale = transform.localScale;
        if (!isBot)
        {
            DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius;
            PlayerEvents.OnScaleUIUpdate?.Invoke();
        }
    }

    /// <summary>봇 전용 레벨업 (DataManager 독립)</summary>
    public void BotLevelUp()
    {
        if (_botScaleLevel >= DataManager.Instance.maxScaleLevel) return;
        _botScaleLevel++;
        QueueScaleChange(BotIncreaseScale(_botScaleLevel, DataManager.Instance.scaleIncreaseTime));
    }

    private IEnumerator BotIncreaseScale(int level, float duration)
    {
        int idx = Mathf.Clamp(level - 1, 0, DataManager.Instance.scalePerLevel.Length - 1);
        Vector3 start  = currentScale;
        Vector3 target = originalScale * DataManager.Instance.scalePerLevel[idx];

        if (softBody3D != null) softBody3D.DisableCloth();

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            currentScale = Vector3.Lerp(start, target, t / duration);
            transform.localScale = currentScale;
            yield return null;
        }
        transform.localScale = currentScale = target;

        if (softBody3D != null) StartCoroutine(softBody3D.EnableAndRebuildCloth());

        // 스케일 변경 후 CharacterController 캡슐이 지면에 박히지 않도록 재정렬
        AIPlayerMovement aiMovement = GetComponent<AIPlayerMovement>();
        if (aiMovement != null) aiMovement.RecenterCC();
    }

    private void Update()
    {
        // ���̷�Ʈ ������ ���� (����� 1~5)
        if (Input.GetKeyDown(KeyCode.Alpha1)) StartCoroutine(ChangeScaleToLevel(1, DataManager.Instance.scaleIncreaseTime));
        if (Input.GetKeyDown(KeyCode.Alpha2)) StartCoroutine(ChangeScaleToLevel(2, DataManager.Instance.scaleIncreaseTime));
        if (Input.GetKeyDown(KeyCode.Alpha3)) StartCoroutine(ChangeScaleToLevel(3, DataManager.Instance.scaleIncreaseTime));
        if (Input.GetKeyDown(KeyCode.Alpha4)) StartCoroutine(ChangeScaleToLevel(4, DataManager.Instance.scaleIncreaseTime));
        if (Input.GetKeyDown(KeyCode.Alpha5)) StartCoroutine(ChangeScaleToLevel(5, DataManager.Instance.scaleIncreaseTime));
    }

    public void CheckScaleUp()
    {
        if (DataManager.Instance.absorbedJellyCount >= DataManager.Instance.scaleLevelUpExp)
        {
            QueueScaleChange(IncreaseScale(DataManager.Instance.scaleIncreaseTime));

            if (DataManager.Instance.playerCurrentScaleLevel >= 3)
                playerController.jumpForce = playerController.originalJumpForce + DataManager.Instance.IncreaseJumpForceValue;
            else
                playerController.jumpForce = playerController.originalJumpForce;

            DataManager.Instance.absorbedJellyCount = 0;
        }
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

    private IEnumerator IncreaseScale(float increaseTime)
    {
        if (DataManager.Instance.playerCurrentScaleLevel == DataManager.Instance.maxScaleLevel) yield break;

        DataManager.Instance.playerCurrentScaleLevel++;

        if (softBody3D != null) softBody3D.DisableCloth();

        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayScaleUpSound();
        UIPoolManager.Instance?.SpawnUI(UIType.ScaleIncrease);

        PlayerEvents.OnCameraScaleIncreased?.Invoke();

        Vector3 startScale = currentScale;
        int levelIndex = Mathf.Clamp(DataManager.Instance.playerCurrentScaleLevel - 1, 0, DataManager.Instance.maxScaleLevel - 1);
        Vector3 targetScale = originalScale * DataManager.Instance.scalePerLevel[levelIndex];

        float t = 0f;
        while (t < increaseTime)
        {
            t += Time.deltaTime;
            currentScale = Vector3.Lerp(startScale, targetScale, t / increaseTime);
            transform.localScale = currentScale;
            yield return null;
        }

        transform.localScale = targetScale;
        currentScale = targetScale;
        playerController.moveSpeed *= 1.2f;

        if (softBody3D != null) StartCoroutine(softBody3D.EnableAndRebuildCloth());

        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    public IEnumerator DecreaseScale(float decreaseTime)
    {
        if (DataManager.Instance.playerCurrentScaleLevel <= 1) yield break;

        DataManager.Instance.playerCurrentScaleLevel--;
        DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius + DataManager.Instance.detectPlusRadiusPerLevel * (DataManager.Instance.playerCurrentScaleLevel - 1);

        if (softBody3D != null) softBody3D.DisableCloth();
        UIPoolManager.Instance?.SpawnUI(UIType.MilkScaleDecrease);

        Vector3 startScale = currentScale;
        int levelIndex = Mathf.Clamp(DataManager.Instance.playerCurrentScaleLevel - 1, 0, DataManager.Instance.maxScaleLevel - 1);
        Vector3 targetScale = originalScale * DataManager.Instance.scalePerLevel[levelIndex];

        float t = 0f;
        while (t < decreaseTime)
        {
            t += Time.deltaTime;
            currentScale = Vector3.Lerp(startScale, targetScale, t / decreaseTime);
            transform.localScale = currentScale;
            yield return null;
        }

        transform.localScale = targetScale;
        currentScale = targetScale;
        PlayerEvents.OnScaleUIUpdate?.Invoke();

        if (playerController != null) playerController.moveSpeed /= 1.2f;
        if (softBody3D != null) StartCoroutine(softBody3D.EnableAndRebuildCloth());
    }

    public IEnumerator ChangeScaleToLevel(int targetLevel, float transitionTime)
    {
        targetLevel = Mathf.Clamp(targetLevel, 1, DataManager.Instance.maxScaleLevel);
        if (DataManager.Instance.playerCurrentScaleLevel == targetLevel) yield break;

        int previousLevel = DataManager.Instance.playerCurrentScaleLevel;

        if (softBody3D != null) softBody3D.DisableCloth();

        Vector3 startScale = currentScale;
        Vector3 targetScale = originalScale * DataManager.Instance.scalePerLevel[targetLevel - 1];

        float t = 0f;
        while (t < transitionTime)
        {
            t += Time.deltaTime;
            currentScale = Vector3.Lerp(startScale, targetScale, t / transitionTime);
            transform.localScale = currentScale;
            yield return null;
        }

        transform.localScale = targetScale;
        currentScale = targetScale;


        DataManager.Instance.playerCurrentScaleLevel = targetLevel;
        DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius + DataManager.Instance.detectPlusRadiusPerLevel * (targetLevel - 1);

        if (playerController != null)
        {
            playerController.jumpForce = targetLevel >= 3 ? playerController.originalJumpForce + DataManager.Instance.IncreaseJumpForceValue : playerController.originalJumpForce;
            int levelDiff = targetLevel - previousLevel;
            playerController.moveSpeed *= Mathf.Pow(1.2f, levelDiff);
        }


        if (DataManager.Instance.playerCurrentScaleLevel > previousLevel)
        {
            PlayerEvents.OnScaleUIUpdate?.Invoke();
        }
        else
        {
            PlayerEvents.OnScaleUIUpdate?.Invoke();
        }


        if (softBody3D != null) StartCoroutine(softBody3D.EnableAndRebuildCloth());
    }

    /// <summary>봇 전용: DataManager 없이 특정 레벨로 즉시 스케일 변경</summary>
    public void SetScaleLevel(int level)
    {
        int idx = Mathf.Clamp(level - 1, 0, DataManager.Instance.scalePerLevel.Length - 1);
        float s = DataManager.Instance.scalePerLevel[idx];
        currentScale = Vector3.one * s;
        transform.localScale = currentScale;
    }

    public void ResetScale()
    {
        DataManager.Instance.absorbedJellyCount = 0;
        PlayerEvents.OnCameraOrthoSizeChanged?.Invoke(6.1f);

        StopAllCoroutines();
        scaleQueue.Clear();
        isScaling = false;

        StartCoroutine(ChangeScaleToLevel(1, DataManager.Instance.scaleDecreaseDuration));
        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }
}