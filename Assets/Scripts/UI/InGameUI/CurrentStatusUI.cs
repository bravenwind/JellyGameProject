using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CurrentStatusUI : MonoBehaviour
{
    public Image currentColorImage;
    public Text currentScaleText;

    public void UpdateColorUI()
    {
        currentColorImage.color = DataManager.Instance.GetCurrentDisplayColor();
    }

    public void UpdateScaleUI()
    {
        currentScaleText.text = DataManager.Instance.playerCurrentScale.ToString("F2");
    }
}
