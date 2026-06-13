using UnityEngine;
using UnityEngine.SceneManagement;

public class TopRightButtonUI : MonoBehaviour
{
    public UIManager uiManager;

    public void OnSettingsBtnClicked()
    {
        uiManager.SetState(UIState.Settings);
        PlaySFXAudio.Instance.PlayButtonClickSound();
    }
}
