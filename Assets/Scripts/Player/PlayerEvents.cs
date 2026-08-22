using System;
using UnityEngine;

public static class ColorExtensions
{
}

public static class PlayerEvents
{
    // 젤리 흡수 시 발생하는 이벤트
    public static Action<JellyColorType> OnJellyAbsorbed;

    // 색상 변경 이벤트 (RYB) — 지배 색상 타입 + 현재 RYB 값
    public static Action<JellyColorType, RYBColor> OnColorChanged;

    // UI 업데이트 요청 이벤트
    public static Action OnColorUIUpdate;
    public static Action OnScaleUIUpdate;

    public static Action OnPlayDingEffect;
    public static Action OnCameraScaleIncreased;
    public static Action OnCameraScaleDecreased;

    public static Action<int> OnCameraLevelChanged;
    public static Action<float> OnCameraOrthoSizeChanged;

    // 목표 색상 이벤트
    public static Action<bool> OnTargetColorChecked;
    public static Action OnPlayerResetRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        OnJellyAbsorbed = null;
        OnColorChanged = null;
        OnColorUIUpdate = null;
        OnScaleUIUpdate = null;
        OnPlayDingEffect = null;
        OnCameraScaleIncreased = null;
        OnCameraScaleDecreased = null;
        OnCameraLevelChanged = null;
        OnCameraOrthoSizeChanged = null;
        OnTargetColorChecked = null;
        OnPlayerResetRequested = null;
    }
}
