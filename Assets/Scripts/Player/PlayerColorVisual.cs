using System;
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
    public float blendTime = 0.5f;

    [Header("Material_Property")]
    public string BaseColor_01Property = "_BaseColor_01";
    public string BaseColor_02Property = "_BaseColor_02";
    public string FresnelProperty = "_FresnelColor";

    private Coroutine currentCoroutine;

    // ── RYB Color State (이전의 IEntityBridge.RYBColor 역할) ──
    public RYBColor CurrentRYB { get; private set; } = RYBColor.white;

    // ── Events ──
    public event Action<JellyColorType, RYBColor, Color> OnColorApplied;

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

        CurrentRYB = RYBColor.white;
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
        CurrentRYB = RYBColor.white;
        OnColorApplied?.Invoke(JellyColorType.White, RYBColor.white, Color.white);

        Color white = Color.white;
        currentCoroutine = StartCoroutine(BlendColor(white, GetLighterColor(white, baseColor02Lightness), white, blendTime));
    }

    private void ApplyJellyColor(JellyColorType type)
    {
        RYBColor effect = DataManager.Instance.GetJellyRYBEffect(type);
        RYBColor nextRYB = CurrentRYB.Add(effect);
        CurrentRYB = nextRYB;

        Color visualTarget = nextRYB.ToRGB();
        JellyColorType dominantType = nextRYB.GetDominantType();
        OnColorApplied?.Invoke(dominantType, nextRYB, visualTarget);

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
    }

    /// <summary>
    /// 네트워크로 받은 최종 색을 그대로 입힌다. 로컬 누적치(CurrentRYB)는 건드리지 않는다.
    ///
    /// ★ 왜 필요한가
    ///   젤리 셰이더의 색은 _BaseColor_01 · _BaseColor_02 · _FresnelColor 세 개다.
    ///   LanBotState는 그중 _FresnelColor 하나만 보내고 받는 쪽에서 머티리얼에 직접
    ///   꽂고 있었다. 그래서 호스트에서는 세 개가 다 바뀌는데 클라에서는 프레넬만
    ///   바뀌고 본체 색은 처음 그대로였다 — "색 요소 중 일부만 적용"의 정체다.
    ///   파생 공식(ApplyJellyColor)과 똑같은 계산을 여기서 재사용해 양쪽을 맞춘다.
    /// </summary>
    public void ApplyNetworkColor(Color visualTarget)
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);

        Color targetBase = DarkenColor(visualTarget, 0.85f);
        Color targetBase02 = GetLighterColor(targetBase, baseColor02Lightness);

        currentCoroutine = StartCoroutine(
            BlendColor(targetBase, targetBase02, visualTarget, blendTime));
    }

    private Color GetLighterColor(Color baseColor, float lightAmount)
        => Color.Lerp(baseColor, Color.white, lightAmount);

    private Color DarkenColor(Color color, float factor)
        => new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

}
