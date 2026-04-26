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
        float current = GameState.PlayerCurrentScale;
        float min = DataManager.Instance.minScale;
        float max = DataManager.Instance.maxScale;
        needJellyText.text = "Scale : " + current.ToString("F2");
        currentLevelText.text = "Max : " + max.ToString("F1");
        expImage.fillAmount = (current - min) / (max - min);
    }
}
