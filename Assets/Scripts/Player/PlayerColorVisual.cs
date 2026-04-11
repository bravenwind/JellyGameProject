using System.Collections;
using UnityEngine;

public class PlayerColorVisual : MonoBehaviour
{
    public Renderer rend;

    private Color originalBaseColor;
    private Color originalBaseColor_02;
    public Color originalFresnelColor;

    private Color currentBaseColor;
    private Color currentBaseColor_02;
    public Color currentFresnelColor;

    [Header("Color Settings")]
    [Range(0f, 1f)] public float baseColor02Lightness = 0.6f;
    [Tooltip("색상 전환 애니메이션 시간")]
    public float blendTime = 0.5f;

    [Header("Material_Property")]
    public string BaseColor_01Property = "_BaseColor_01";
    public string BaseColor_02Property = "_BaseColor_02";
    public string FresnelProperty = "_FresnelColor";

    public bool isBot = false;
    private RYBColor _botRYBColor = RYBColor.white; // 봇 전용 RYB 색상

    private Coroutine currentCoroutine;

    private void Start()
    {
        if (rend == null) rend = GetComponentInChildren<Renderer>();

        originalBaseColor    = Color.white;
        originalFresnelColor = Color.white;
        originalBaseColor_02 = GetLighterColor(originalBaseColor, baseColor02Lightness);

        currentBaseColor    = originalBaseColor;
        currentFresnelColor = originalFresnelColor;
        currentBaseColor_02 = originalBaseColor_02;

        rend.material.SetColor(BaseColor_01Property, currentBaseColor);
        rend.material.SetColor(BaseColor_02Property, currentBaseColor_02);
        rend.material.SetColor(FresnelProperty,       currentFresnelColor);

        if (!isBot)
        {
            DataManager.Instance.currentRYBColor = RYBColor.white;
            DataManager.Instance.currentDisplayColor = Color.white;
            PlayerEvents.OnColorUIUpdate?.Invoke();
        }
    }

    /// <summary>젤리 흡수 시 호출 — RYB 누산 + 시각화</summary>
    public void HandleJellyAbsorbed(JellyColorType type)
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);

        if (type == JellyColorType.White)
        {
            HandleWhiteJelly();
            return;
        }

        ApplyJellyColor(type);
    }

    private void HandleWhiteJelly()
    {
        // RYB 초기화
        if (isBot)
            _botRYBColor = RYBColor.white;
        else
            DataManager.Instance.ResetRYBColor();

        Color white = Color.white;
        currentCoroutine = StartCoroutine(BlendColor(white, GetLighterColor(white, baseColor02Lightness), white, blendTime));
    }

    private void ApplyJellyColor(JellyColorType type)
    {
        // 1. RYB 누산
        RYBColor effect = DataManager.Instance.GetJellyRYBEffect(type);
        RYBColor baseRYB = isBot ? _botRYBColor : DataManager.Instance.currentRYBColor;
        RYBColor nextRYB = baseRYB.Add(effect);

        if (isBot)
            _botRYBColor = nextRYB;
        else
            DataManager.Instance.currentRYBColor = nextRYB;

        // 2. RYB → RGB 변환 (시각화용)
        Color visualTarget = nextRYB.ToRGB();

        // 3. 목표 색상 판정 (플레이어만)
        if (!isBot)
        {
            DataManager.Instance.currentDisplayColor = visualTarget;
            JellyColorType dominantType = nextRYB.GetDominantType();

            // GameModeManager가 목표 판정을 처리하도록 이벤트 발행
            PlayerEvents.OnColorChanged?.Invoke(dominantType, nextRYB);
        }

        // 4. 시각화 — 약간 어둡게 + BaseColor_02는 밝게
        Color targetBase = DarkenColor(visualTarget, 0.85f);
        Color targetFresnel = visualTarget;
        Color targetBase02 = GetLighterColor(targetBase, baseColor02Lightness);

        currentCoroutine = StartCoroutine(BlendColor(targetBase, targetBase02, targetFresnel, blendTime));
    }

    private IEnumerator BlendColor(Color targetBase, Color targetBase_02, Color targetFresnel, float time)
    {
        Color startBase = currentBaseColor;
        Color startBase_02 = currentBaseColor_02;
        Color startFresnel = currentFresnelColor;

        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            float progress = t / time;

            currentBaseColor = Color.Lerp(startBase, targetBase, progress);
            currentBaseColor_02 = Color.Lerp(startBase_02, targetBase_02, progress);
            currentFresnelColor = Color.Lerp(startFresnel, targetFresnel, progress);

            rend.material.SetColor(BaseColor_01Property, currentBaseColor);
            rend.material.SetColor(BaseColor_02Property, currentBaseColor_02);
            rend.material.SetColor(FresnelProperty, currentFresnelColor);

            yield return null;
        }

        currentBaseColor = targetBase;
        currentBaseColor_02 = targetBase_02;
        currentFresnelColor = targetFresnel;

        rend.material.SetColor(BaseColor_01Property, currentBaseColor);
        rend.material.SetColor(BaseColor_02Property, currentBaseColor_02);
        rend.material.SetColor(FresnelProperty, currentFresnelColor);

        PlayerEvents.OnColorUIUpdate?.Invoke();
    }

    private Color GetLighterColor(Color baseColor, float lightAmount)
        => Color.Lerp(baseColor, Color.white, lightAmount);

    private Color DarkenColor(Color color, float factor)
        => new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

    public void ResetColor()
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);

        if (isBot)
            _botRYBColor = RYBColor.white;
        else
            DataManager.Instance.ResetRYBColor();

        currentCoroutine = StartCoroutine(BlendColor(originalBaseColor, originalBaseColor_02, originalFresnelColor, 0.25f));
        PlayerEvents.OnColorUIUpdate?.Invoke();
    }

    /// <summary>봇의 현재 RYB 색상 (외부 참조용)</summary>
    public RYBColor BotRYBColor => _botRYBColor;
}
