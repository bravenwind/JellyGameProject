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

        // ★ 구독만 하고 끝내면 안 된다 — 현재값을 한 번 당겨와야 한다
        //   GameState를 채우는 쪽(PlayerScaleController → PlayerBridge)은 내 캐릭터가
        //   스폰될 때 발화하는데, 이 HUD는 씬에 처음부터 있다. 그 사이 순서에 기대면
        //   디자인타임 플레이스홀더가 그대로 화면에 남는다.
        //   "구독 + 초기값 당기기"는 언제나 짝으로 간다.
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
        if (currentScaleText == null)
            return;

        // ★ 아직 모르는 값은 그리지 않는다
        //   GameState.PlayerCurrentScale은 내 캐릭터가 스폰돼 OnScaleSettled가
        //   발화해야 채워진다. 그전까지는 0인데, 그걸 "0.00"으로 그리면
        //   <b>크기가 0인 것처럼</b> 보인다. 로비→카운트다운 동안 계속 그렇다.
        currentScaleText.text = scale > 0f ? scale.ToString("F2") : Placeholder;
    }

    private const string Placeholder = "-";

    private void OnDisplayColorChanged(Color color)
    {
        if (currentColorImage != null)
            currentColorImage.color = color;
    }
}
