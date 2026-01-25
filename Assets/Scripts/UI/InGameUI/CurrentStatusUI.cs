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

    //public void ChangeColorUI_CurrentJelly()
    //{
    //    for (int i = 0; i < jellyColorImages.Length; i++)
    //    {
    //        // 현재 인덱스의 젤리 타입 가져오기
    //        JellyColorType type = DataManager.Instance.jellyBuffer[i];

    //        switch (type)
    //        {
    //            case JellyColorType.Red:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[0].normal;
    //                break;
    //            case JellyColorType.Green:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[1].normal;
    //                break;
    //            case JellyColorType.Blue:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[2].normal;
    //                break;
    //            case JellyColorType.Cyan:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[3].normal;
    //                break;
    //            case JellyColorType.Magenta:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[4].normal;
    //                break;
    //            case JellyColorType.Yellow:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[5].normal;
    //                break;
    //            case JellyColorType.White:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[6].normal;
    //                break;
    //            case JellyColorType.Black:
    //                jellyColorImages[i].color = DataManager.Instance.enemyJellyColorSets[7].normal;
    //                break;
    //            //필요하다면 default 케이스 추가 (예: 투명하게 하거나 흰색으로 설정)
    //            case JellyColorType.Temp:
    //                jellyColorImages[i].color = new Color(1, 1, 1, 0);
    //                break;
    //        }
    //    }
    //}

    public void ChangeColorUI_TargetColor(Color changeColor)
    {
        targetColorImage.color = changeColor;

        //// 현재 인덱스의 젤리 타입 가져오기
        //JellyColorType type = DataManager.Instance.targetColorSet.colorType;

        //switch (type)
        //{
        //    case JellyColorType.Red:
        //        targetColorImage.color = Color.red;
        //        break;
        //    case JellyColorType.Green:
        //        targetColorImage.color = Color.green;
        //        break;
        //    case JellyColorType.Blue:
        //        targetColorImage.color = Color.blue;
        //        break;
        //    case JellyColorType.Yellow:
        //        targetColorImage.color = Color.yellow;
        //        break;
        //    case JellyColorType.Magenta:
        //        targetColorImage.color = Color.magenta;
        //        break;
        //    case JellyColorType.Cyan:
        //        targetColorImage.color = Color.cyan;
        //        break;
        //    case JellyColorType.White:
        //        targetColorImage.color = Color.white;
        //        break;
        //    case JellyColorType.Black:
        //        targetColorImage.color = Color.black;
        //        break;
        //    case JellyColorType.Temp:
        //        targetColorImage.color = new Color(1, 1, 1, 0);
        //        break;
        //}
    }

    public void ChangeCurrentColorUI()
    {
        if (DataManager.Instance.DetermineCurrentColor(DataManager.Instance.currentColor) == DataManager.Instance.thisGameRangeRule.resultType) 
        {
            checkImage.SetActive(true);
        }
        else
        {
            checkImage.SetActive(false);
        }

        currentColorImage.color = new Color32(DataManager.Instance.currentColor.r, DataManager.Instance.currentColor.g, DataManager.Instance.currentColor.b, 255);
        //currentColorText.text = $"R: {DataManager.Instance.currentColor.r} G: {DataManager.Instance.currentColor.g} B: {DataManager.Instance.currentColor.b}";
    }

    public void ChangeCurrentScaleUI()
    {
        currentScaleText.text = "Lv." + DataManager.Instance.playerCurrentScaleLevel.ToString();
    }
}
