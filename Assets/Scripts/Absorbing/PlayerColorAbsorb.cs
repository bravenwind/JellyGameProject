using System.Collections;
using UnityEngine;

// (ColorExtensions 코드는 그대로 유지)
public static class ColorExtensions
{
    public static Color32 AddRGB(this Color32 current, int r, int g, int b)
    {
        return new Color32(
            (byte)Mathf.Clamp(current.r + r, 0, 255),
            (byte)Mathf.Clamp(current.g + g, 0, 255),
            (byte)Mathf.Clamp(current.b + b, 0, 255),
            current.a
        );
    }
}

public class PlayerColorAbsorb : MonoBehaviour
{
    public Renderer rend;

    private Color32 originalBaseColor;
    private Color32 originalBaseColor_02;
    private Color32 originalSSSColor;
    public Color32 originalFresnelColor;

    private Color32 currentBaseColor;
    private Color32 currentBaseColor_02;
    private Color32 currentSSSColor;
    public Color32 currentFresnelColor;

    private Color32 targetBaseColor;
    private Color32 targetBaseColor_02;
    private Color32 targetSSSColor;
    public Color32 targetFresnelColor;

    public Rigidbody[] rigidbodies;

    [Header("Scale Increase Settings")]
    public Vector3 originalScale;
    private Vector3 currentScale;

    // 🔥 추가: 주인공의 기본 키 (인스펙터에서 젤리 캐릭터 높이에 맞게 조절하세요)
    [Tooltip("캐릭터의 기본 높이 (스케일이 커지면 이 값에 비례해서 감지 높이도 높아집니다)")]
    public float playerBaseHeight = 1.5f;

    [Header("Color Settings")]
    [Tooltip("BaseColor_02가 BaseColor_01에 비해 얼마나 더 연할지 결정합니다. (1에 가까울수록 흰색)")]
    [Range(0f, 1f)]
    public float baseColor02Lightness = 0.6f;

    [Header("Reference")]
    public SoftBody3D softBody3D;
    public JellyCamera jellyCamera;
    public CurrentStatusUI currentStatusUI;
    private MainCamera_Action mainCamera_Action;
    private Coroutine currentFadeCoroutine;
    public UIFollowTarget scaleIncreasedEffect;
    public UIPoolManager uIPoolManager;
    public PlayerController playerController;

    [Header("Material_Property")]
    public string BaseColor_01Property = "_BaseColor_01";
    public string BaseColor_02Property = "_BaseColor_02";
    public string FresnelProperty = "_Fresnel_Color";

    public Transform detectTransform;

    void Start()
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

        rend.material.SetColor(BaseColor_01Property, currentBaseColor);
        rend.material.SetColor(BaseColor_02Property, currentBaseColor_02);
        rend.material.SetColor(FresnelProperty, currentFresnelColor);

        currentStatusUI.ChangeCurrentColorUI();

        DataManager.Instance.currentColor = DataManager.Instance.initialSystemColor;
        DataManager.Instance.targetColor = DataManager.Instance.currentColor;

        originalScale = Vector3.one;
        currentScale = transform.localScale;

        DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius;

        if (Camera.main != null)
            mainCamera_Action = Camera.main.gameObject.GetComponent<MainCamera_Action>();
    }

    private void Update()
    {
        // 🔥 수정됨: OverlapSphere -> OverlapCapsule (원기둥 형태 감지)
        // 현재 캐릭터의 스케일(Y축)을 반영한 높이 계산
        float currentHeight = playerBaseHeight * transform.localScale.y;

        // 바닥 중심점과 머리 위 중심점 계산
        Vector3 bottomPoint = detectTransform.position;
        Vector3 topPoint = detectTransform.position + Vector3.up * currentHeight;

        // Capsule(캡슐) 형태로 감지 (바닥부터 머리까지 반경 detectRadius만큼 감지)
        Collider[] detectedJellies = Physics.OverlapCapsule(bottomPoint, topPoint, DataManager.Instance.detectRadius, DataManager.Instance.detectLayerMask);

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
            if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);

            DataManager.Instance.absorbedJellyCount = 0;
            DataManager.Instance.playerCurrentScaleLevel = 1;
            Camera.main.orthographicSize = 6.1f;

            StartCoroutine(DecreaseScale(1.0f, new Vector3(1.0f, 1.0f, 1.0f)));
            // 🔥 리셋할 때 02번 컬러 원본도 함께 전달
            currentFadeCoroutine = StartCoroutine(BlendColor(originalBaseColor, originalBaseColor_02, originalFresnelColor, 0.25f));
        }
    }

    // 🔥 메인 컬러를 하얀색과 섞어 더 연하게(밝게) 만드는 함수
    private Color32 GetLighterColor(Color32 baseColor, float lightAmount)
    {
        return Color.Lerp(baseColor, Color.white, lightAmount);
    }

    public void AbsorbColor(JellyColorType type)
    {
        DataManager.Instance.absorbedJellyCount++;
        DataManager.Instance.currentScore += 100;
        if (jellyCamera != null) jellyCamera.PlayDing();

        if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);

        Vector3Int effect = DataManager.Instance.GetJellyEffect(type);

        if (type == JellyColorType.White)
        {
            targetBaseColor = new Color32(255, 255, 255, 255);
            targetBaseColor_02 = new Color32(255, 255, 255, 255); // 흰색일 땐 둘 다 흰색
            targetFresnelColor = new Color32(255, 255, 255, 255);
            DataManager.Instance.targetColor = targetBaseColor;
        }
        else
        {
            ApplyJellyColor(new Vector3(effect.x, effect.y, effect.z));
        }

        // 🔥 targetBaseColor_02도 함께 블렌딩 코루틴으로 전달
        currentFadeCoroutine = StartCoroutine(BlendColor(targetBaseColor, targetBaseColor_02, targetFresnelColor, 0.5f));

        if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
        {
            DataManager.Instance.missions[1].missionCleared = true;
        }

        if (DataManager.Instance.absorbedJellyCount >= DataManager.Instance.scaleLevelUpExp)
        {
            if (DataManager.Instance.playerCurrentScaleLevel < DataManager.Instance.maxScaleLevel)
            {
                uIPoolManager.SpawnUI(scaleIncreasedEffect, transform);
                StartCoroutine(IncreaseScale(DataManager.Instance.scaleIncreaseTime));

                DataManager.Instance.detectRadius = DataManager.Instance.originalDetectRadius + 1.0f * DataManager.Instance.playerCurrentScaleLevel;
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
    }

    void ApplyJellyColor(Vector3 change)
    {
        Color32 baseTarget = DataManager.Instance.targetColor;
        Color32 nextColor = baseTarget.AddRGB((int)change.x, (int)change.y, (int)change.z);
        JellyColorType determinedType = DataManager.Instance.DetermineCurrentColor(nextColor);

        if (determinedType != JellyColorType.None)
        {
            switch (determinedType)
            {
                case JellyColorType.Red: nextColor = Color.red; break;
                case JellyColorType.Green: nextColor = Color.green; break;
                case JellyColorType.Blue: nextColor = Color.blue; break;
                case JellyColorType.Cyan: nextColor = Color.cyan; break;
                case JellyColorType.Magenta: nextColor = Color.magenta; break;
                case JellyColorType.Yellow: nextColor = Color.yellow; break;
            }

            if (determinedType == DataManager.Instance.thisGameRangeRule.resultType)
            {
                Debug.Log($"🎉 [게임 클리어] 목표 색상인 {determinedType} 달성! 🎉");
            }
        }

        targetBaseColor = nextColor;
        targetFresnelColor = nextColor;

        int darknessStep = DataManager.Instance.darknessStep;
        targetBaseColor = targetBaseColor.AddRGB(darknessStep, darknessStep, darknessStep);
        targetFresnelColor = targetFresnelColor.AddRGB(darknessStep, darknessStep, darknessStep);

        // 🔥 BaseColor_02 타겟 설정 (BaseColor_01보다 baseColor02Lightness 만큼 하얀색에 가깝게)
        targetBaseColor_02 = GetLighterColor(targetBaseColor, baseColor02Lightness);

        DataManager.Instance.targetColor = targetBaseColor;
    }

    // 🔥 파라미터에 targetBase_02 추가
    IEnumerator BlendColor(Color targetBase, Color targetBase_02, Color targetFresnel, float time)
    {
        Color startBase = currentBaseColor;
        Color startBase_02 = currentBaseColor_02; // 🔥
        Color startFresnel = currentFresnelColor;

        if (PlayFXAudio.Instance != null) PlayFXAudio.Instance.PlayColorMixSound();

        float t = 0f;

        while (t < time)
        {
            t += Time.deltaTime;
            float progress = t / time;

            currentBaseColor = Color.Lerp(startBase, targetBase, progress);
            currentBaseColor_02 = Color.Lerp(startBase_02, targetBase_02, progress); // 🔥
            currentFresnelColor = Color.Lerp(startFresnel, targetFresnel, progress);

            rend.material.SetColor(BaseColor_01Property, currentBaseColor);
            rend.material.SetColor(BaseColor_02Property, currentBaseColor_02); // 🔥
            rend.material.SetColor(FresnelProperty, currentFresnelColor);

            yield return null;
        }

        currentBaseColor = DataManager.Instance.targetColor;
        currentBaseColor_02 = targetBase_02; // 🔥 목표색으로 정확히 안착
        currentFresnelColor = DataManager.Instance.targetColor;

        rend.material.SetColor(BaseColor_01Property, currentBaseColor);
        rend.material.SetColor(BaseColor_02Property, currentBaseColor_02); // 🔥
        rend.material.SetColor(FresnelProperty, currentFresnelColor);

        DataManager.Instance.currentColor = currentBaseColor;
        currentStatusUI.ChangeCurrentColorUI();
    }

    // (IncreaseScale, DecreaseScale, OnDrawGizmos 생략 - 이전과 동일)
    IEnumerator IncreaseScale(float increaseTime)
    {
        if (softBody3D != null) softBody3D.DisableCloth();

        if (PlayFXAudio.Instance != null) PlayFXAudio.Instance.PlayScaleUpSound();

        Vector3 startScale = currentScale;
        int levelIndex = Mathf.Clamp(DataManager.Instance.playerCurrentScaleLevel - 1, 0, DataManager.Instance.maxScaleLevel - 1);
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
        if (DataManager.Instance != null && detectTransform != null)
        {
            Gizmos.color = Color.green;

            float currentHeight = playerBaseHeight * transform.localScale.y;
            float radius = DataManager.Instance.detectRadius;

            Vector3 bottomPoint = detectTransform.position;
            Vector3 topPoint = detectTransform.position + Vector3.up * currentHeight;

            // 바닥 원, 천장 원 그리기
            Gizmos.DrawWireSphere(bottomPoint, radius);
            Gizmos.DrawWireSphere(topPoint, radius);

            // 기둥 선 연결 (좌, 우, 앞, 뒤)
            Gizmos.DrawLine(bottomPoint + Vector3.left * radius, topPoint + Vector3.left * radius);
            Gizmos.DrawLine(bottomPoint + Vector3.right * radius, topPoint + Vector3.right * radius);
            Gizmos.DrawLine(bottomPoint + Vector3.forward * radius, topPoint + Vector3.forward * radius);
            Gizmos.DrawLine(bottomPoint + Vector3.back * radius, topPoint + Vector3.back * radius);
        }
    }
}