using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelUI : MonoBehaviour
{
    public Image expImage;
    public TMP_Text needJellyText;
    public TMP_Text currentLevelText;

    public void Start()
    {
        ChangeLevelUI();
    }

    public void ChangeLevelUI()
    {
        needJellyText.text = "Until Next Level : " + (DataManager.Instance.scaleLevelUpExp - DataManager.Instance.absorbedJellyCount).ToString();
        currentLevelText.text = "Level : " + (DataManager.Instance.playerCurrentScaleLevel).ToString();
        expImage.fillAmount = (float)DataManager.Instance.absorbedJellyCount / DataManager.Instance.scaleLevelUpExp;
    }
}
