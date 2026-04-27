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

    private IEntityBridge _bridge;
    private Coroutine currentCoroutine;

    private void Awake()
    {
        _bridge = GetComponentInParent<IEntityBridge>();
    }

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

        if (_bridge != null)
        {
            _bridge.RYBColor = RYBColor.white;
            _bridge.OnColorUIUpdate();
        }
    }

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
        _bridge?.ResetRYBColor();

        Color white = Color.white;
        currentCoroutine = StartCoroutine(BlendColor(white, GetLighterColor(white, baseColor02Lightness), white, blendTime));
    }

    private void ApplyJellyColor(JellyColorType type)
    {
        RYBColor effect = DataManager.Instance.GetJellyRYBEffect(type);
        RYBColor baseRYB = _bridge?.RYBColor ?? RYBColor.white;
        RYBColor nextRYB = baseRYB.Add(effect);

        if (_bridge != null) _bridge.RYBColor = nextRYB;

        Color visualTarget = nextRYB.ToRGB();

        JellyColorType dominantType = nextRYB.GetDominantType();
        _bridge?.OnColorApplied(dominantType, nextRYB, visualTarget);

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

        _bridge?.OnColorUIUpdate();
    }

    private Color GetLighterColor(Color baseColor, float lightAmount)
        => Color.Lerp(baseColor, Color.white, lightAmount);

    private Color DarkenColor(Color color, float factor)
        => new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

    public void ResetColor()
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);

        _bridge?.ResetRYBColor();

        currentCoroutine = StartCoroutine(BlendColor(originalBaseColor, originalBaseColor_02, originalFresnelColor, 0.25f));
        _bridge?.OnColorUIUpdate();
    }

    public RYBColor BotRYBColor => _bridge?.RYBColor ?? RYBColor.white;
}
