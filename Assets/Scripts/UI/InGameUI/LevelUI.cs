using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelUI : MonoBehaviour
{
    public Image expImage;
    public TMP_Text needJellyText;
    public TMP_Text currentLevelText;

    private void OnEnable()
    {
        GameState.OnScaleChanged += OnScaleChanged;
        Refresh(GameState.PlayerCurrentScale);
    }

    private void OnDisable()
    {
        GameState.OnScaleChanged -= OnScaleChanged;
    }

    private void OnScaleChanged(float scale) => Refresh(scale);

    private void Refresh(float current)
    {
        float min = DataManager.Instance.minScale;
        float max = DataManager.Instance.maxScale;
        needJellyText.text = "Scale : " + current.ToString("F2");
        currentLevelText.text = "Max : " + max.ToString("F1");
        expImage.fillAmount = (current - min) / (max - min);
    }
}
