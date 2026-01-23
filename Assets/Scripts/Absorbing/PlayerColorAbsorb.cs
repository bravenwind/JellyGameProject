using System.Collections;
using UnityEngine;

public static class ColorExtensions
{
    public static Color32 AddRGB(this Color32 current, int r, int g, int b)
    {
        Debug.Log($"현재 : {current}, {r}, {g}, {b}");

        return new Color32(
            (byte)Mathf.Clamp(current.r + r, 0, 255),
            (byte)Mathf.Clamp(current.g + g, 0, 255),
            (byte)Mathf.Clamp(current.b + b, 0, 255),
            current.a // 알파값 유지
        );
    }
}

public class PlayerColorAbsorb : MonoBehaviour
{
    public Renderer rend;

    // 색상 변수들
    private Color32 originalBaseColor;
    // public Color32 originalEmissionColor;
    private Color32 originalSSSColor;
    public Color32 originalFresnelColor;

    private Color32 currentBaseColor;
    // public Color32 currentEmissionColor;
    private Color32 currentSSSColor;
    public Color32 currentFresnelColor;

    private Color32 targetBaseColor;
    // public Color32 targetEmissionColor;
    private Color32 targetSSSColor;
    public Color32 targetFresnelColor;

    public Rigidbody[] rigidbodies;

    public Vector3 originalScale;
    private Vector3 currentScale;
    public SoftBody3D softBody3D;

    public Cloth playerCloth;
    public JellyCamera jellyCamera;
    public CurrentStatusUI currentStatusUI;

    private MainCamera_Action mainCamera_Action;
    private Coroutine currentFadeCoroutine;

    //public UIFollowTarget followTarget;
    public UIFollowTarget scaleIncreasedEffect;
    public UIPoolManager uIPoolManager;
    public PlayerController playerController;

    public float originalDetectRadius;
    public float detectRadius;
    public LayerMask detectLayerMask;

    // ✨ 셰이더 프로퍼티 멤버 변수
    // public string emissionProperty = "_Emission";
    public string BaseColor_01Property = "_BaseColor_01";
    public string BaseColor_02Property = "_BaseColor_02";
    public string FresnelProperty = "_Fresnel_Color";

    void Start()
    {
        if (rend == null) rend = GetComponentInChildren<Renderer>();

        // originalEmissionColor = DataManager.Instance.initialColor;
        originalBaseColor = DataManager.Instance.initialColor; // Emission 대신 BaseColor 사용
        originalFresnelColor = DataManager.Instance.initialColor;

        // currentEmissionColor = originalEmissionColor;
        currentBaseColor = originalBaseColor;
        currentFresnelColor = originalFresnelColor;

        // ✨ 수정: Emission 대신 BaseColor_01Property 적용
        // rend.material.SetColor(emissionProperty, currentEmissionColor);
        rend.material.SetColor(BaseColor_01Property, currentBaseColor);
        rend.material.SetColor(FresnelProperty, currentFresnelColor);

        // DataManager.Instance.currentColor = currentEmissionColor;
        // DataManager.Instance.targetColor = currentEmissionColor;
        DataManager.Instance.currentColor = currentBaseColor;
        DataManager.Instance.targetColor = currentBaseColor;

        originalScale = Vector3.one;
        currentScale = transform.localScale;

        detectRadius = originalDetectRadius;

        playerCloth = GetComponentInChildren<Cloth>();
        currentStatusUI.ChangeCurrentColorUI();

        if (Camera.main != null)
            mainCamera_Action = Camera.main.gameObject.GetComponent<MainCamera_Action>();
    }

    private void Update()
    {
        Collider[] detectedJellies = Physics.OverlapSphere(transform.position, detectRadius, detectLayerMask);

        if (detectedJellies.Length > 0)
        {
            foreach (Collider c in detectedJellies)
            {
                JellyColliderAbsorb jca = c.gameObject.GetComponentInParent<JellyColliderAbsorb>();
                if (jca != null && jca.absorbing == false)
                {
                    jca.StartAbsorb(transform);
                    c.isTrigger = true;
                }
            }
        }

        // 디버그용 리셋
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("원상복구 시도");
            if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);

            DataManager.Instance.absorbedJellyCount = 0;
            DataManager.Instance.playerCurrentScaleLevel = 1;

            Camera.main.orthographicSize = 6.1f;

            StartCoroutine(DecreaseScale(1.0f, new Vector3(1.0f, 1.0f, 1.0f)));
            // currentFadeCoroutine = StartCoroutine(BlendColor(originalEmissionColor, originalFresnelColor, 0.25f));
            currentFadeCoroutine = StartCoroutine(BlendColor(originalBaseColor, originalFresnelColor, 0.25f));
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        DataManager.Instance.absorbedJellyCount++;
        DataManager.Instance.currentScore += 100;
        jellyCamera.PlayDing();

        if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);

        Vector3Int effect = DataManager.Instance.GetJellyEffect(type);

        if (type == JellyColorType.White)
        {
            // targetEmissionColor = new Color32(255, 255, 255, 255);
            targetBaseColor = new Color32(255, 255, 255, 255);
            targetFresnelColor = new Color32(255, 255, 255, 255);

            // DataManager.Instance.targetColor = targetEmissionColor;
            DataManager.Instance.targetColor = targetBaseColor;
        }
        else
        {
            ApplyJellyColor(new Vector3(effect.x, effect.y, effect.z));
        }

        // currentFadeCoroutine = StartCoroutine(BlendColor(targetEmissionColor, targetFresnelColor, 0.5f));
        currentFadeCoroutine = StartCoroutine(BlendColor(targetBaseColor, targetFresnelColor, 0.5f));

        if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
        {
            DataManager.Instance.missions[1].missionCleared = true;
        }

        if (DataManager.Instance.absorbedJellyCount >= DataManager.Instance.scaleLevelUpExp)
        {
            if (DataManager.Instance.playerCurrentScaleLevel < DataManager.Instance.maxScaleLevel)
            {
                uIPoolManager.SpawnUI(scaleIncreasedEffect, transform);
                StartCoroutine(IncreaseScale(0.5f));

                detectRadius = originalDetectRadius + 1.0f * DataManager.Instance.playerCurrentScaleLevel;

                DataManager.Instance.playerCurrentScaleLevel++;
            }

            if (DataManager.Instance.playerCurrentScaleLevel >= 3)
            {
                playerController.jumpForce = playerController.originalJumpForce + DataManager.Instance.IncreaseJumpForceValue;
            }
            else
            {
                playerController.jumpForce = playerController.originalJumpForce;
            }

            DataManager.Instance.absorbedJellyCount = 0;

            currentStatusUI.ChangeCurrentScaleUI();

            if (mainCamera_Action != null) mainCamera_Action.ScaleChanged();
        }

        Debug.Log($"젤리 흡수: {type}");
    }

    void ApplyJellyColor(Vector3 change)
    {
        Color32 baseTarget = DataManager.Instance.targetColor;

        // targetEmissionColor = baseTarget.AddRGB((int)change.x, (int)change.y, (int)change.z);
        targetBaseColor = baseTarget.AddRGB((int)change.x, (int)change.y, (int)change.z);
        targetFresnelColor = baseTarget.AddRGB((int)change.x, (int)change.y, (int)change.z);

        int darknessStep = DataManager.Instance.darknessStep;
        // targetEmissionColor = targetEmissionColor.AddRGB(darknessStep, darknessStep, darknessStep);
        targetBaseColor = targetBaseColor.AddRGB(darknessStep, darknessStep, darknessStep);
        targetFresnelColor = targetFresnelColor.AddRGB(darknessStep, darknessStep, darknessStep);

        // DataManager.Instance.targetColor = targetEmissionColor;
        DataManager.Instance.targetColor = targetBaseColor;
    }

    // ✨ 파라미터 이름 targetEmission -> targetBase로 변경
    IEnumerator BlendColor(Color targetBase, Color targetFresnel, float time)
    {
        // Color startEmission = currentEmissionColor;
        Color startBase = currentBaseColor;
        Color startFresnel = currentFresnelColor;

        PlayFXAudio.Instance.PlayColorMixSound();

        float t = 0f;

        while (t < time)
        {
            t += Time.deltaTime;
            float progress = t / time;

            // currentEmissionColor = Color.Lerp(startEmission, targetEmission, progress);
            currentBaseColor = Color.Lerp(startBase, targetBase, progress);
            currentFresnelColor = Color.Lerp(startFresnel, targetFresnel, progress);

            // ✨ 수정: Emission 대신 BaseColor_01Property 적용
            // rend.material.SetColor(emissionProperty, currentEmissionColor);
            rend.material.SetColor(BaseColor_01Property, currentBaseColor);
            rend.material.SetColor(FresnelProperty, currentFresnelColor);

            yield return null;
        }

        // currentEmissionColor = DataManager.Instance.targetColor;
        currentBaseColor = DataManager.Instance.targetColor;
        currentFresnelColor = DataManager.Instance.targetColor;

        // ✨ 수정: Emission 대신 BaseColor_01Property 적용
        // rend.material.SetColor(emissionProperty, currentEmissionColor);
        rend.material.SetColor(BaseColor_01Property, currentBaseColor);
        rend.material.SetColor(FresnelProperty, currentFresnelColor);

        // DataManager.Instance.currentColor = currentEmissionColor;
        DataManager.Instance.currentColor = currentBaseColor;
        currentStatusUI.ChangeCurrentColorUI();
    }

    IEnumerator IncreaseScale(float increaseTime)
    {
        if (softBody3D != null) softBody3D.DisableCloth();

        PlayFXAudio.Instance.PlayScaleUpSound();

        Vector3 startScale = currentScale;
        int levelIndex = Mathf.Clamp(DataManager.Instance.playerCurrentScaleLevel - 1, 0, DataManager.Instance.maxScaleLevel - 1);
        Debug.Log(levelIndex);
        Vector3 targetScale = originalScale * DataManager.Instance.scaleMultiplyPerLevel[levelIndex];

        float t = 0f;

        while (t < increaseTime)
        {
            t += Time.deltaTime;
            float progress = t / increaseTime;

            currentScale = Vector3.Lerp(startScale, targetScale, progress);
            transform.localScale = currentScale;

            yield return null;
        }

        transform.localScale = targetScale;
        currentScale = targetScale;
        playerController.moveSpeed *= 1.2f;

        if (softBody3D != null)
        {
            StartCoroutine(softBody3D.EnableAndRebuildCloth());
        }
    }

    IEnumerator DecreaseScale(float decreaseTime, Vector3 targetScale)
    {
        if (softBody3D != null) softBody3D.DisableCloth();

        Vector3 startScale = currentScale;

        float t = 0f;

        while (t < decreaseTime)
        {
            t += Time.deltaTime;
            float progress = t / decreaseTime;

            currentScale = Vector3.Lerp(startScale, targetScale, progress);
            transform.localScale = currentScale;

            yield return null;
        }

        transform.localScale = targetScale;
        currentScale = targetScale;

        if (softBody3D != null)
        {
            StartCoroutine(softBody3D.EnableAndRebuildCloth());
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}