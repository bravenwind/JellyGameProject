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
    // 젤리를 먹었을 때 발생하는 이벤트
    public static Action<JellyColorType> OnJellyAbsorbed;

    // UI 업데이트 요청 이벤트
    public static Action OnColorUIUpdate;
    public static Action OnScaleUIUpdate;

    public static Action OnPlayDingEffect;
    public static Action OnCameraScaleIncreased;
    public static Action OnCameraScaleDecreased;

    public static Action<int> OnCameraLevelChanged;
    public static Action<float> OnCameraOrthoSizeChanged;

    // 게임 상태 이벤트
    public static Action<bool> OnTargetColorChecked;
    public static Action OnPlayerResetRequested; // R키 눌렀을 때
}
