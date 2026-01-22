using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
public class QuestStarUI : MonoBehaviour
{
    [Header("Star Images (size 3)")]
    public Image[] stars;
    public Image[] miniStars;

    [Header("Mission Texts")]
    public TMP_Text mission1;
    public TMP_Text mission2;
    public TMP_Text mission3;

    [Range(0, 3)]
    public int Index = 0;

    public Text resultText;
    bool isFailed = true;

    private void Start()
    {
        SetMission1();
        SetMission2();
        SetMission3();
    }

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

    public void SetMission1()
    {
        JellyColorType missionColorType = DataManager.Instance.thisGameRangeRule.resultType;
        string missionText = "";

        switch (missionColorType)
        {
            case JellyColorType.Red:
                missionText = "빨간색 젤리 만들기";
                break;
            case JellyColorType.Green:
                missionText = "초록색 젤리 만들기";
                break;
            case JellyColorType.Blue:
                missionText = "파란색 젤리 만들기";
                break;
            case JellyColorType.Cyan:
                missionText = "시안(청록) 젤리 만들기";
                break;
            case JellyColorType.Magenta:
                missionText = "마젠타(자홍) 젤리 만들기";
                break;
            case JellyColorType.Yellow:
                missionText = "노란색 젤리 만들기";
                break;
            case JellyColorType.White:
                missionText = "흰색 젤리 만들기";
                break;
            case JellyColorType.Black:
                missionText = "검은색 젤리 만들기";
                break;
            case JellyColorType.Temp:
                missionText = "임시 젤리 만들기";
                break;
            case JellyColorType.None:
                missionText = "지정된 미션 없음";
                break;
            default:
                missionText = "알 수 없는 젤리 만들기";
                break;
        }
        mission1.text = missionText;
    }

    public void SetMission2()
    {
        mission2.text = $"크기 {DataManager.Instance.targetScaleLevel} 단계로 만들기";
    }

    public void SetMission3()
    {
        mission3.text = $"{DataManager.Instance.targetTime}초 내 클리어하기";   
    }
}