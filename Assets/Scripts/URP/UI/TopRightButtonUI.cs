using UnityEngine;
using UnityEngine.SceneManagement;

public class TopRightButtonUI : MonoBehaviour
{
    public UIManager uiManager;
    
    public void OnMenuBtnClicked()
    {
        uiManager.SetState(UIState.Menu);
    }

    public void OnSettingsBtnClicked()
    {
        uiManager.SetState(UIState.Settings);
    }

    public void OnPauseBtnClicked()
    {
        uiManager.SetState(UIState.Pause);
    }
}
