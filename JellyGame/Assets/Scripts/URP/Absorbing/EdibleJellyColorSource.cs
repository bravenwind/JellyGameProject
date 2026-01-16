using UnityEngine;

public class EdibleJellyColorSource : JellyColorSource
{
    private WorldToScreenGUI worldToScreenGUI;
    protected override void Start()
    { 
        base.Start(); 
        jellyColor = rend.material.GetColor("_BaseColor");
        worldToScreenGUI = GetComponent<WorldToScreenGUI>();
        switch (colorType)
        {
            case JellyColorType.Red:
                worldToScreenGUI.displayText = $"{DataManager.Instance.redPlusColor}";
                break;

            case JellyColorType.Green:
                worldToScreenGUI.displayText = $"{DataManager.Instance.greenPlusColor}";
                break;

            case JellyColorType.Blue:
                worldToScreenGUI.displayText = $"{DataManager.Instance.bluePlusColor}";
                break;

            case JellyColorType.Yellow:
                worldToScreenGUI.displayText = $"{DataManager.Instance.yellowPlusColor}";
                break;

            case JellyColorType.Cyan:
                worldToScreenGUI.displayText = $"{DataManager.Instance.cyanPlusColor}";
                break;

            case JellyColorType.Magenta:
                worldToScreenGUI.displayText = $"{DataManager.Instance.magentaPlusColor}";
                break;

            case JellyColorType.White:
                worldToScreenGUI.displayText = $"{DataManager.Instance.whitePlusColor}";
                break;
        }
    }
}
