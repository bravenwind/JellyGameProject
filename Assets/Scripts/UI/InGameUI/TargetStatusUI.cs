using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TargetStatusUI : MonoBehaviour
{
    public Text targetStatusText;
    public TMP_Text missionText1;
    public TMP_Text missionText2;
    public TMP_Text missionText3;

    // 1. 타입을 Color32로 변경
    private Color32[] colors = new Color32[7];

    private void Start()
    {
        //// Color32는 Color(float)에서 자동으로 변환됩니다.
        //colors[0] = Color.red;     // (255, 0, 0, 255)
        //colors[1] = Color.green;   // (0, 255, 0, 255)
        //colors[2] = Color.blue;    // (0, 0, 255, 255)
        //colors[3] = Color.yellow;
        //colors[4] = Color.magenta;
        //colors[5] = Color.cyan;
        //colors[6] = Color.white;

        //// [중요 수정] 정수형 Random.Range에서 두 번째 인자는 '제외'됩니다.
        //// 배열의 모든 요소를 포함하려면 Length-1이 아니라 Length를 써야 합니다.
        //int index = Random.Range(0, colors.Length);

        //Color32 selectedColor = colors[index];
        //DataManager.Instance.targetColor = selectedColor;

        //// 이미지 색상 적용
        //colorImage.color = selectedColor;

        // 2. 텍스트 포맷팅: (255, 0, 0) 형식으로 직접 지정
        //colorText.text = $"R: {selectedColor.r} G: {selectedColor.g} B: {selectedColor.b}";
        DataManager.ColorRangeRule thisGameRangeRule = DataManager.Instance.thisGameRangeRule;
        string targetColorText = "";
        switch (DataManager.Instance.thisGameRangeRule.resultType)
        {
            case JellyColorType.Red:
                targetColorText = "빨강";
                break;
            case JellyColorType.Green:
                targetColorText = "초록";
                break;
            case JellyColorType.Blue:
                targetColorText = "파랑";
                break;
            case JellyColorType.Cyan:
                targetColorText = "청록"; // 게임 분위기에 따라 "하늘 젤리 "로 변경하셔도 좋습니다.
                break;
            case JellyColorType.Magenta:
                targetColorText = "자홍"; // 게임 분위기에 따라 "보라 젤리 " 또는 "분홍 젤리 "로 변경하셔도 좋습니다.
                break;
            case JellyColorType.Yellow:
                targetColorText = "노랑";
                break;
            case JellyColorType.White:
                targetColorText = "하양";
                break;
            case JellyColorType.Black:
                targetColorText = "검정";
                break;
            case JellyColorType.Temp:
                targetColorText = "임시"; // 디버그/임시용
                break;
            case JellyColorType.None:
                targetColorText = "알 수 없는"; // 또는 "" (빈 문자열)
                break;
        }
        string targetScaleLevelText = "Lv." + DataManager.Instance.targetScaleLevel;
        targetStatusText.text = targetColorText + " 젤리 " + targetScaleLevelText + " 만들기";
        missionText1.text = targetColorText + "색 젤리 만들기";
        missionText2.text = "크기 " + targetScaleLevelText + "레벨" + " 만들기";
        missionText3.text = DataManager.Instance.targetTime + "초 이내 클리어하기";
    }
}