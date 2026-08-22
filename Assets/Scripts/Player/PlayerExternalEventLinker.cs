using UnityEngine;

public class PlayerExternalEventLinker : MonoBehaviour
{
    [Header("UI & Camera References")]
    public JellyCamera jellyCamera;
    public MainCamera_Action mainCamera_Action;
    public GameObject checkImage;

    private void OnEnable()
    {
        PlayerEvents.OnPlayDingEffect += PlayDing;
        PlayerEvents.OnCameraScaleIncreased += CameraChange_Increase;
        PlayerEvents.OnCameraLevelChanged += ChangeCameraLevel;
        PlayerEvents.OnTargetColorChecked += UpdateCheckImage;
        // [Z3] OnCameraOrthoSizeChanged 구독 제거 — MainCamera_Action이 같은 이벤트를
        // 이미 구독해 처리한다. 여기서도 Camera.main.orthographicSize를 직접 대입하면
        // 같은 값이 두 번 쓰이고(중복 라이터), 카메라 제어가 컨트롤러(MainCamera_Action)
        // 바깥으로 새어 나간다. 카메라 크기의 라이터는 MainCamera_Action 단일 출처로.
    }

    private void OnDisable()
    {
        PlayerEvents.OnPlayDingEffect -= PlayDing;
        PlayerEvents.OnCameraScaleIncreased -= CameraChange_Increase;
        PlayerEvents.OnCameraLevelChanged -= ChangeCameraLevel;
        PlayerEvents.OnTargetColorChecked -= UpdateCheckImage;
    }

    private void PlayDing() { if (jellyCamera != null) jellyCamera.PlayDing(); }
    private void CameraChange_Increase() { if (mainCamera_Action != null) mainCamera_Action.ScaleIncreased(); }
    private void ChangeCameraLevel(int level) { if (mainCamera_Action != null) mainCamera_Action.ChangeCameraSizeToLevel(level); }
    private void UpdateCheckImage(bool isCorrectColor) { if (checkImage != null) checkImage.SetActive(isCorrectColor); }
}
