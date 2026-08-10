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
        PlaySFXAudio.Instance.PlayButtonClickSound();
        Debug.Log("���� ����");
        Application.Quit();
    }

    public void OnToTitleBtnClicked()
    {
        PlaySFXAudio.Instance.PlayButtonClickSound();

        // [LAN] 경기 중 이탈 금지. 페이드가 시작된 뒤에 막으면 화면만 까맣게 남는다.
        if (!JellyNet.LanSceneFlow.CanLeaveMatch)
            return;

        uiManager.StartCoroutine(uiManager.SceneFade("FadeOut"));
    }

    public void OnMenuBtnClicked()
    {
        PlaySFXAudio.Instance.PlayButtonClickSound();
        uiManager.SetState(UIState.InGame);
    }
}
