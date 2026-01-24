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
