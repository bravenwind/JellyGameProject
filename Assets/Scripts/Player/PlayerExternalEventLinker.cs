using UnityEngine;

public class PlayerExternalEventLinker : MonoBehaviour
{
    [Header("UI & Camera References")]
    public JellyCamera jellyCamera;
    public MainCamera_Action mainCamera_Action;
    public UIPoolManager uIPoolManager;
    public GameObject checkImage;

    private void OnEnable()
    {
        PlayerEvents.OnPlayDingEffect += PlayDing;
        PlayerEvents.OnCameraScaleIncreased += CameraChange_Increase;
        PlayerEvents.OnCameraLevelChanged += ChangeCameraLevel;
        PlayerEvents.OnTargetColorChecked += UpdateCheckImage;
        PlayerEvents.OnCameraOrthoSizeChanged += ChangeCameraOrthoSize;
    }

    private void OnDisable()
    {
        PlayerEvents.OnPlayDingEffect -= PlayDing;
        PlayerEvents.OnCameraScaleIncreased -= CameraChange_Increase;
        PlayerEvents.OnCameraLevelChanged -= ChangeCameraLevel;
        PlayerEvents.OnTargetColorChecked -= UpdateCheckImage;
        PlayerEvents.OnCameraOrthoSizeChanged -= ChangeCameraOrthoSize;
    }

    private void PlayDing() { if (jellyCamera != null) jellyCamera.PlayDing(); }
    private void CameraChange_Increase() { if (mainCamera_Action != null) mainCamera_Action.ScaleIncreased(); }
    private void ChangeCameraLevel(int level) { if (mainCamera_Action != null) mainCamera_Action.ChangeCameraSizeToLevel(level); }
    private void UpdateCheckImage(bool isCorrectColor) { if (checkImage != null) checkImage.SetActive(isCorrectColor); }
    private void ChangeCameraOrthoSize(float size) { if (Camera.main != null) Camera.main.orthographicSize = size; }
}
