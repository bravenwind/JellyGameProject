using UnityEngine;
using UnityEngine.UI;

public class CurrentStatusUI : MonoBehaviour
{
    public Image currentColorImage;
    public Text currentScaleText;

    private void OnEnable()
    {
        GameState.OnScaleChanged += OnScaleChanged;
        GameState.OnDisplayColorChanged += OnDisplayColorChanged;
    }

    private void OnDisable()
    {
        GameState.OnScaleChanged -= OnScaleChanged;
        GameState.OnDisplayColorChanged -= OnDisplayColorChanged;
    }

    private void OnScaleChanged(float scale)
    {
        currentScaleText.text = scale.ToString("F2");
    }

    private void OnDisplayColorChanged(Color color)
    {
        currentColorImage.color = color;
    }
}
