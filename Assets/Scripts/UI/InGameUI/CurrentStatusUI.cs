using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CurrentStatusUI : MonoBehaviour
{
    public Image[] jellyColorImages;
    public Image currentColorImage;
    public Text currentColorText;
    public Text currentScaleText;
    private Image targetColorImage;

    public GameObject checkImage;

    public void UpdateColorUI()
    {
        currentColorImage.color = DataManager.Instance.GetCurrentDisplayColor();
    }

    public void UpdateScaleUI()
    {
        currentScaleText.text = "Lv." + DataManager.Instance.playerCurrentScaleLevel.ToString();
    }
}
