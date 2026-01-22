using UnityEngine;
using UnityEngine.SceneManagement;

public class TopRightButtonUI : MonoBehaviour
{
    public UIManager uiManager;
    
    public void OnMenuBtnClicked()
    {
        uiManager.SetState(UIState.Menu);
        PlayFXAudio.Instance.PlayButtonClickSound();
    }

    public void OnSettingsBtnClicked()
    {
        uiManager.SetState(UIState.Settings);
        PlayFXAudio.Instance.PlayButtonClickSound();
    }

    public void OnPauseBtnClicked()
    {
        uiManager.SetState(UIState.Pause);
    }
}
