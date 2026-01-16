using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ColorUI : MonoBehaviour
{
    public Image colorImage;
    public TMP_Text colorText;

    public void ChangeColor(Color32 color)
    {
        colorImage.color = color;
        colorText.text = $"({color.r}, {color.g}, {color.b})";
    }
}
