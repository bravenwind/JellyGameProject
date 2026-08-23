using UnityEngine;

public class PlayerExternalEventLinker : MonoBehaviour
{
    [Header("UI & Camera References")]
    public MainCamera_Action mainCamera_Action;

    private void OnEnable()
    {
        PlayerEvents.OnCameraScaleIncreased += CameraChange_Increase;
        // [Z3] OnCameraOrthoSizeChanged 구독 제거 — MainCamera_Action이 같은 이벤트를
        // 이미 구독해 처리한다. 여기서도 Camera.main.orthographicSize를 직접 대입하면
        // 같은 값이 두 번 쓰이고(중복 라이터), 카메라 제어가 컨트롤러(MainCamera_Action)
        // 바깥으로 새어 나간다. 카메라 크기의 라이터는 MainCamera_Action 단일 출처로.
    }

    private void OnDisable()
    {
        PlayerEvents.OnCameraScaleIncreased -= CameraChange_Increase;
    }

    private void CameraChange_Increase() { if (mainCamera_Action != null) mainCamera_Action.ScaleIncreased(); }
}
