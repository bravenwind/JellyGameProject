using UnityEngine;
using System.Collections.Generic;
public class MenuUI : MonoBehaviour
{
    public UIManager uiManager;

    public void OnGameQuitBtnClicked()
    {
        Debug.Log("게임 종료");
        Application.Quit();
    }

    public void OnToTitleBtnClicked()
    {
        StartCoroutine(uiManager.SceneFade("FadeOut"));
    }

    public void OnMenuBtnClicked()
    {
        uiManager.SetState(UIState.InGame);
    }
}
