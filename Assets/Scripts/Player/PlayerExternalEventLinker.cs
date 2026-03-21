using UnityEngine;

public class PlayerExternalEventLinker : MonoBehaviour
{
    [Header("UI & Camera References")]
    public CurrentStatusUI currentStatusUI;
    public JellyCamera jellyCamera;
    public MainCamera_Action mainCamera_Action;
    public UIPoolManager uIPoolManager;
    public GameObject checkImage;

    private void OnEnable()
    {
        // 플레이어 내부에서 발생한 소식을 듣고 외부 UI/카메라를 대신 조작해줍니다.
        PlayerEvents.OnColorUIUpdate += UpdateColorUI;
        PlayerEvents.OnScaleUIUpdate += UpdateScaleUI;
        PlayerEvents.OnPlayDingEffect += PlayDing;
        PlayerEvents.OnCameraScaleIncreased += CameraChange_Increase;
        PlayerEvents.OnCameraLevelChanged += ChangeCameraLevel;
        PlayerEvents.OnTargetColorChecked += UpdateCheckImage;
        PlayerEvents.OnCameraOrthoSizeChanged += ChangeCameraOrthoSize;
    }

    private void OnDisable()
    {
        PlayerEvents.OnColorUIUpdate -= UpdateColorUI;
        PlayerEvents.OnScaleUIUpdate -= UpdateScaleUI;
        PlayerEvents.OnPlayDingEffect -= PlayDing;
        PlayerEvents.OnCameraScaleIncreased -= CameraChange_Increase;
        PlayerEvents.OnCameraLevelChanged -= ChangeCameraLevel;
        PlayerEvents.OnTargetColorChecked -= UpdateCheckImage;
        PlayerEvents.OnCameraOrthoSizeChanged -= ChangeCameraOrthoSize;
    }

    private void UpdateColorUI() { if (currentStatusUI != null) currentStatusUI.UpdateColorUI(); }
    private void UpdateScaleUI() { if (currentStatusUI != null) currentStatusUI.UpdateScaleUI(); }
    private void PlayDing() { if (jellyCamera != null) jellyCamera.PlayDing(); }
    private void CameraChange_Increase() { if (mainCamera_Action != null) mainCamera_Action.ScaleIncreased(); }
    private void ChangeCameraLevel(int level) { if (mainCamera_Action != null) mainCamera_Action.ChangeCameraSizeToLevel(level); }
    private void UpdateCheckImage(bool isCorrectColor) { if (checkImage != null) checkImage.SetActive(isCorrectColor); }
    private void ChangeCameraOrthoSize(float size) { if (Camera.main != null) Camera.main.orthographicSize = size; }
}