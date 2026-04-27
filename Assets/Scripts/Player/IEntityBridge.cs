using UnityEngine;

public interface IEntityBridge
{
    // ── Scale ──
    void OnScaleInit(float scaleValue);
    void OnGrowEffect(bool playEffect);
    void OnShrinkEffect();
    void OnScaleThresholdUp();
    void OnScaleThresholdDown();
    void OnScaleCompleted(float scaleValue, PlayerController pc);
    void OnScaleUIUpdate();
    void OnScaleReset();
    void OnPostScalePhysics();

    // ── Color ──
    RYBColor RYBColor { get; set; }
    void ResetRYBColor();
    void OnColorApplied(JellyColorType dominantType, RYBColor ryb, Color displayColor);
    void OnColorUIUpdate();

    // ── Absorb ──
    void OnJellyScored();
}
