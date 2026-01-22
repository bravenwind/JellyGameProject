using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
public class QuestStarUI : MonoBehaviour
{
    [Header("Star Images (size 3)")]
    public Image[] stars;
    public Image[] miniStars;

    [Range(0, 3)]
    public int Index = 0;

    public Text resultText;
    bool isFailed = true;

    public void ApplySuccess(int index)
    {
        isFailed = false;

        int count = Mathf.Clamp(index, 0, 3);
        SetStars(count);
        SetResultText(isFailed);
    }

    public void ApplyFail()
    {
        isFailed = true;

        SetStars(0);
        SetResultText(isFailed);
    }

    public void SetStarsByQuestCount(int questIndexCount)
    {
        int count = Mathf.Clamp(questIndexCount, 0, 3);
        SetStars(count);
    }

    private void SetResultText(bool failed)
    {
        if (resultText == null) return;
        resultText.text = failed ? "Fail" : "Success";
    }
    public void Fail()
    {
        SetStars(0);
    }

    private void SetStars(int activeCount)
    {
        if (stars == null) return;

        activeCount = Mathf.Clamp(activeCount, 0, 3);

        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] == null) continue;
            stars[i].gameObject.SetActive(i < activeCount);
            miniStars[i].gameObject.SetActive(i < activeCount);
        }
    }

    public void OnRestartBtnClicked()
    {
        SceneManager.LoadScene("ResourceApplyScene_Temp");
        PlayFXAudio.Instance.PlayButtonClickSound();
    }

    public void OnMenuBtnClicked()
    {
        SceneManager.LoadScene("Main");
        PlayFXAudio.Instance.PlayButtonClickSound();
    }
}
