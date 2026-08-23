using System;
using UnityEngine;

public static class PlayerEvents
{
    // 카메라 확대/축소 요청
    public static Action OnCameraScaleIncreased;
    public static Action OnCameraScaleDecreased;

    // 카메라 크기 직접 지정
    public static Action<float> OnCameraOrthoSizeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        OnCameraScaleIncreased = null;
        OnCameraScaleDecreased = null;
        OnCameraOrthoSizeChanged = null;
    }
}
