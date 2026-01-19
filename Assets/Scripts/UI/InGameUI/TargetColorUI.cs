using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TargetColorUI : MonoBehaviour
{
    public Image colorImage;
    public TMP_Text colorText;

    // 1. 타입을 Color32로 변경
    private Color32[] colors = new Color32[7];

    private void Start()
    {
        // Color32는 Color(float)에서 자동으로 변환됩니다.
        colors[0] = Color.red;     // (255, 0, 0, 255)
        colors[1] = Color.green;   // (0, 255, 0, 255)
        colors[2] = Color.blue;    // (0, 0, 255, 255)
        colors[3] = Color.yellow;
        colors[4] = Color.magenta;
        colors[5] = Color.cyan;
        colors[6] = Color.white;

        // [중요 수정] 정수형 Random.Range에서 두 번째 인자는 '제외'됩니다.
        // 배열의 모든 요소를 포함하려면 Length-1이 아니라 Length를 써야 합니다.
        int index = Random.Range(0, colors.Length);

        Color32 selectedColor = colors[index];
        DataManager.Instance.targetColor = selectedColor;

        // 이미지 색상 적용
        colorImage.color = selectedColor;

        // 2. 텍스트 포맷팅: (255, 0, 0) 형식으로 직접 지정
        colorText.text = $"({selectedColor.r}, {selectedColor.g}, {selectedColor.b})" + $" ±30";
    }
}