using System.Collections;
using UnityEngine;

public class PlayerColorVisual : MonoBehaviour
{
    public Renderer rend;

    private Color32 originalBaseColor;
    private Color32 originalBaseColor_02;
    public Color32 originalFresnelColor;

    private Color32 currentBaseColor;
    private Color32 currentBaseColor_02;
    public Color32 currentFresnelColor;

    private Color32 targetBaseColor;
    private Color32 targetBaseColor_02;
    public Color32 targetFresnelColor;

    [Header("Color Settings")]
    [Range(0f, 1f)] public float baseColor02Lightness = 0.6f;

    [Header("Material_Property")]
    public string BaseColor_01Property = "_BaseColor_01";
    public string BaseColor_02Property = "_BaseColor_02";
    public string FresnelProperty = "_FresnelColor";

    private Coroutine currentCoroutine;

    private void Start()
    {
        if (rend == null) rend = GetComponentInChildren<Renderer>();

        originalBaseColor = DataManager.Instance.initialViewColor;
        originalFresnelColor = DataManager.Instance.initialViewColor;
        originalBaseColor_02 = GetLighterColor(originalBaseColor, baseColor02Lightness);

        currentBaseColor = originalBaseColor;
        currentFresnelColor = originalFresnelColor;
        currentBaseColor_02 = originalBaseColor_02;

        DataManager.Instance.currentColor = DataManager.Instance.initialViewColor;
        DataManager.Instance.targetColor = DataManager.Instance.currentColor;
        DataManager.Instance.systemCurrentColor = DataManager.Instance.initialViewColor;

        rend.material.SetColor(BaseColor_01Property, currentBaseColor);
        rend.material.SetColor(BaseColor_02Property, currentBaseColor_02);
        rend.material.SetColor(FresnelProperty, currentFresnelColor);

        DataManager.Instance.currentColor = DataManager.Instance.initialSystemColor;
        DataManager.Instance.targetColor = DataManager.Instance.currentColor;

        PlayerEvents.OnColorUIUpdate?.Invoke();
    }

    public void HandleJellyAbsorbed(JellyColorType type)
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);

        Vector3Int effect = DataManager.Instance.GetJellyEffect(type);

        if (type == JellyColorType.White)
        {
            targetBaseColor = new Color32(255, 255, 255, 255);
            targetBaseColor_02 = new Color32(255, 255, 255, 255);
            targetFresnelColor = new Color32(255, 255, 255, 255);
            DataManager.Instance.currentColor = targetBaseColor;
            DataManager.Instance.systemCurrentColor = targetBaseColor;
        }
        else
        {
            ApplyJellyColor(new Vector3(effect.x, effect.y, effect.z));
        }
    }

    private void ApplyJellyColor(Vector3 change)
    {
        Color32 baseSystem = DataManager.Instance.systemCurrentColor;
        Color32 nextSystemColor = baseSystem.AddRGB((int)change.x, (int)change.y, (int)change.z);
        DataManager.Instance.systemCurrentColor = nextSystemColor;

        JellyColorType determinedType = DataManager.Instance.DetermineCurrentColor(nextSystemColor);
        Color32 visualTarget = nextSystemColor;

        if (determinedType != JellyColorType.None)
        {
            switch (determinedType)
            {
                case JellyColorType.Red: visualTarget = Color.red; break;
                case JellyColorType.Green: visualTarget = Color.green; break;
                case JellyColorType.Blue: visualTarget = Color.blue; break;
                case JellyColorType.Cyan: visualTarget = Color.cyan; break;
                case JellyColorType.Magenta: visualTarget = Color.magenta; break;
                case JellyColorType.Yellow: visualTarget = Color.yellow; break;
            }
        }

        // 체크 이미지 UI 업데이트 요청
        if (determinedType == DataManager.Instance.thisGameRangeRule.resultType)
        {
            PlayerEvents.OnTargetColorChecked?.Invoke(true);
            Debug.Log($"[게임 클리어] 목표 색상인 {determinedType} 달성!");
        }
        else
        {
            PlayerEvents.OnTargetColorChecked?.Invoke(false);
        }

        targetBaseColor = visualTarget;
        targetFresnelColor = visualTarget;

        int darknessStep = DataManager.Instance.darknessStep;
        targetBaseColor = targetBaseColor.AddRGB(darknessStep, darknessStep, darknessStep);
        targetFresnelColor = targetFresnelColor.AddRGB(darknessStep, darknessStep, darknessStep);
        targetBaseColor_02 = GetLighterColor(targetBaseColor, baseColor02Lightness);

        currentCoroutine = StartCoroutine(BlendColor(targetBaseColor, targetBaseColor_02, targetFresnelColor, 0.5f));
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

        DataManager.Instance.currentColor = currentBaseColor;
        DataManager.Instance.targetColor = currentBaseColor;

        PlayerEvents.OnColorUIUpdate?.Invoke();
    }

    private Color32 GetLighterColor(Color32 baseColor, float lightAmount)
    {
        return Color.Lerp(baseColor, Color.white, lightAmount);
    }

    public void ResetColor()
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(BlendColor(originalBaseColor, originalBaseColor_02, originalFresnelColor, 0.25f));
        PlayerEvents.OnColorUIUpdate?.Invoke();
    }
}