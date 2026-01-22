using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class MenuUI : MonoBehaviour
{
    public UIManager uiManager;
    public TMP_Text sceneBtnText;

    void Start()
    {

    }

    public void OnGameQuitBtnClicked()
    {
        PlayFXAudio.Instance.PlayButtonClickSound();
        Debug.Log("게임 종료");
        Application.Quit();
    }

    public void OnToTitleBtnClicked()
    {
        PlayFXAudio.Instance.PlayButtonClickSound();
        StartCoroutine(uiManager.SceneFade("FadeOut"));
    }

    public void OnMenuBtnClicked()
    {
        PlayFXAudio.Instance.PlayButtonClickSound();
        uiManager.SetState(UIState.InGame);
    }
}
