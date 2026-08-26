using System;
using UnityEngine;

public static class PlayerEvents
{
    // 카메라 확대/축소 요청
    public static Action OnCameraScaleIncreased;
    public static Action OnCameraScaleDecreased;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        OnCameraScaleIncreased = null;
        OnCameraScaleDecreased = null;
    }
}
