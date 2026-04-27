using UnityEngine;

public class PlayerBridge : MonoBehaviour, IEntityBridge
{
    // ── Scale ──
    public void OnScaleInit(float scaleValue)
    {
        GameState.DetectRadius = DataManager.Instance.originalDetectRadius;
        GameState.PlayerCurrentScale = scaleValue;
        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    public void OnGrowEffect(bool playEffect)
    {
        if (playEffect)
        {
            if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayScaleUpSound();
            UIPoolManager.Instance?.SpawnUI(UIType.ScaleIncrease);
        }
    }

    public void OnShrinkEffect()
    {
        UIPoolManager.Instance?.SpawnUI(UIType.MilkScaleDecrease);
    }

    public void OnScaleThresholdUp() => PlayerEvents.OnCameraScaleIncreased?.Invoke();
    public void OnScaleThresholdDown() => PlayerEvents.OnCameraScaleDecreased?.Invoke();

    public void OnScaleCompleted(float scaleValue, PlayerController pc)
    {
        if (pc != null)
        {
            pc.jumpForce = scaleValue >= DataManager.Instance.jumpScaleThreshold
                ? pc.originalJumpForce + DataManager.Instance.IncreaseJumpForceValue
                : pc.originalJumpForce;
        }
        GameState.DetectRadius = DataManager.Instance.originalDetectRadius
            + (scaleValue - 1f) * DataManager.Instance.detectPlusRadiusPerLevel;
        GameState.PlayerCurrentScale = scaleValue;
    }

    public void OnScaleUIUpdate() => PlayerEvents.OnScaleUIUpdate?.Invoke();

    public void OnScaleReset()
    {
        PlayerEvents.OnCameraOrthoSizeChanged?.Invoke(6.1f);
        GameState.DetectRadius = DataManager.Instance.originalDetectRadius;
        GameState.PlayerCurrentScale = 2f;
        PlayerEvents.OnScaleUIUpdate?.Invoke();
    }

    public void OnPostScalePhysics() { }

    // ── Color ──
    public RYBColor RYBColor
    {
        get => GameState.CurrentRYBColor;
        set => GameState.CurrentRYBColor = value;
    }

    public void ResetRYBColor() => GameState.ResetRYBColor();

    public void OnColorApplied(JellyColorType dominantType, RYBColor ryb, Color displayColor)
    {
        GameState.CurrentDisplayColor = displayColor;
        PlayerEvents.OnColorChanged?.Invoke(dominantType, ryb);
    }

    public void OnColorUIUpdate() => PlayerEvents.OnColorUIUpdate?.Invoke();

    // ── Absorb ──
    public void OnJellyScored()
    {
        GameState.CurrentScore += DataManager.Instance.scorePerJelly;
        GetComponent<NetworkPlayerSync>().SyncScore(GameState.CurrentScore);
        UIPoolManager.Instance?.SpawnUI(UIType.JellyEat);
        if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayColorMixSound();
        if (GameState.CurrentScore >= DataManager.Instance.targetScore)
            DataManager.Instance.missions[1].missionCleared = true;
    }
}
