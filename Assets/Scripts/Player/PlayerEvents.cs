using System;
using UnityEngine;

public static class ColorExtensions
{
    public static Color32 AddRGB(this Color32 current, int r, int g, int b)
    {
        return new Color32(
            (byte)Mathf.Clamp(current.r + r, 0, 255),
            (byte)Mathf.Clamp(current.g + g, 0, 255),
            (byte)Mathf.Clamp(current.b + b, 0, 255),
            current.a
        );
    }
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
}
