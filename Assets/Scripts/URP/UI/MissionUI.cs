using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionUI : MonoBehaviour
{
    public Sprite notClearedStar;
    public Sprite clearedStar;

    public TMP_Text[] missionTexts;
    public Image[] missionImages;

    private void Start()
    {
        for (int i = 0; i < missionTexts.Length; i++)
        {
            missionTexts[i].text = DataManager.Instance.missions[i].missionName;
            missionImages[i].sprite = notClearedStar;
        }
    }

    public void ChangeMissionUI()
    {
        for (int i = 0; i < DataManager.Instance.missions.Length; i++)
        {
            if (DataManager.Instance.missions[i].missionCleared)
            {
                missionImages[i].sprite = clearedStar;
            }
        }
    }
}
