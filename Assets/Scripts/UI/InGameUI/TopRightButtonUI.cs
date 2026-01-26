using UnityEngine;
using UnityEngine.SceneManagement;

public class TopRightButtonUI : MonoBehaviour
{
    public UIManager uiManager;
    
    public void OnMenuBtnClicked()
    {
        uiManager.SetState(UIState.Menu);
        PlaySFXAudio.Instance.PlayButtonClickSound();
    }

    public void OnHelpBtnClicked()
    {
        uiManager.SetState(UIState.Help);
        PlaySFXAudio.Instance.PlayButtonClickSound();
    }

    public void OnHelpExitBtnClicked()
    {
        uiManager.SetState(UIState.InGame);
        PlaySFXAudio.Instance.PlayButtonClickSound();
    }

    private void OnApplicationPause(bool pause)
    {
        
    }

    public void OnSettingsBtnClicked()
    {
        uiManager.SetState(UIState.Settings);
        PlaySFXAudio.Instance.PlayButtonClickSound();
    }

    public void OnPauseBtnClicked()
    {
        uiManager.SetState(UIState.Pause);
    }
}
