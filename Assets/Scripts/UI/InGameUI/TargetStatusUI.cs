using UnityEngine;
using UnityEngine.UI;

public class TargetStatusUI : MonoBehaviour
{
    public Text targetStatusText;
    public Text missionText1;
    public Text missionText2;
    public Text missionText3;
    public Image targetImage;
    public Text targetScaleText;

    private void Start()
    {
        UpdateTargetDisplay();
    }

    private void OnEnable()
    {
        // GameModeManager의 라운드 변경 시 갱신
        PlayerEvents.OnColorUIUpdate += UpdateTargetDisplay;
    }

    private void OnDisable()
    {
        PlayerEvents.OnColorUIUpdate -= UpdateTargetDisplay;
    }

    private void UpdateTargetDisplay()
    {
        //string targetColorText = GameModeManager.GetColorName(gm.TargetColor);
        //Color targetRGB = RYBColor.GetTargetRGB(gm.TargetColor);

        //// 목표 색상 아이콘
        //if (targetImage != null)
        //    targetImage.color = targetRGB;

        //// 스케일 레벨
        //string targetScaleLevelText = "Lv." + DataManager.Instance.targetScaleLevel;
        //if (targetScaleText != null)
        //    targetScaleText.text = targetScaleLevelText;

        //// 목표 상태 텍스트
        //if (targetStatusText != null)
        //    targetStatusText.text = $"{targetColorText} 색의 {targetScaleLevelText} 가 되어야해!";

        //// 미션 텍스트
        //if (missionText1 != null)
        //    missionText1.text = $"{targetColorText}색 젤리 만들기";
        //if (missionText2 != null)
        //    missionText2.text = $"순도 {gm.CurrentPurityThreshold * 100f:F0}% 이상 달성";
        //if (missionText3 != null)
        //    missionText3.text = $"목표 {gm.CycleCount}번째 생존 중";
    }
}
