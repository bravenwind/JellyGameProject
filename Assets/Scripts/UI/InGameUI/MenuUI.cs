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
        Debug.Log("게임 종료");
        Application.Quit();
    }

    public void OnToTitleBtnClicked()
    {
        StartCoroutine(uiManager.SceneFade("FadeOut"));
    }

    public void OnMenuBtnClicked()
    {

    }
}
