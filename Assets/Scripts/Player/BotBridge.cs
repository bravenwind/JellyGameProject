using UnityEngine;

public class BotBridge : MonoBehaviour, IEntityBridge
{
    private RYBColor _rybColor = RYBColor.white;

    // ── Scale (봇은 이벤트/UI 없음) ──
    public void OnScaleInit(float scaleValue) { }
    public void OnGrowEffect(bool playEffect) { }
    public void OnShrinkEffect() { }
    public void OnScaleThresholdUp() { }
    public void OnScaleThresholdDown() { }
    public void OnScaleCompleted(float scaleValue, PlayerController pc) { }
    public void OnScaleUIUpdate() { }
    public void OnScaleReset() { }

    public void OnPostScalePhysics()
    {
        GetComponent<AIPlayerMovement>()?.RecenterCC();
    }

    // ── Color (봇은 로컬 RYB 상태만 관리) ──
    public RYBColor RYBColor
    {
        get => _rybColor;
        set => _rybColor = value;
    }

    public void ResetRYBColor() => _rybColor = RYBColor.white;
    public void OnColorApplied(JellyColorType dominantType, RYBColor ryb, Color displayColor) { }
    public void OnColorUIUpdate() { }

    // ── Absorb ──
    public void OnJellyScored()
    {
        AIPlayerMovement ai = GetComponentInParent<AIPlayerMovement>();
        if (ai != null) ai.currentScore += DataManager.Instance.scorePerJelly;
    }
}
