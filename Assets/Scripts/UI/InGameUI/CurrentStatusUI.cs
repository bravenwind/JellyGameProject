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

        // [W5] 현재값 시딩 — 스폰 스케일(2f)이 GameState 기본값과 같으면 setter의
        // Approximately 가드에 걸려 이벤트가 발화하지 않고, 색은 첫 흡수 전까지
        // 이벤트가 아예 없다. 구독 직후 현재값으로 한 번 그려 디자인타임
        // 플레이스홀더가 그대로 보이는 문제를 막는다. (ScoreUI의 OnEnable 패턴과 동일)
        OnScaleChanged(GameState.PlayerCurrentScale);
        OnDisplayColorChanged(GameState.CurrentDisplayColor);
    }

    private void OnDisable()
    {
        GameState.OnScaleChanged -= OnScaleChanged;
        GameState.OnDisplayColorChanged -= OnDisplayColorChanged;
    }

    private void OnScaleChanged(float scale)
    {
        if (currentScaleText != null)
            currentScaleText.text = scale.ToString("F2");
    }

    private void OnDisplayColorChanged(Color color)
    {
        if (currentColorImage != null)
            currentColorImage.color = color;
    }
}
