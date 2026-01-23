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
    public Color32 originalEmissionColor;
    private Color32 originalSSSColor;
    public Color32 originalFresnelColor;
               
    private Color32 currentBaseColor;
    public Color32 currentEmissionColor;
    private Color32 currentSSSColor;
    public Color32 currentFresnelColor;
               
    private Color32 targetBaseColor;
    public Color32 targetEmissionColor;
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

    //private void OnControllerColliderHit(ControllerColliderHit hit)
    //{
    //    if (hit.gameObject.CompareTag("Edible"))
    //    {
    //        JellyColliderAbsorb jca = hit.gameObject.GetComponentInParent<JellyColliderAbsorb>();
    //        if (jca != null)
    //        {
    //            jca.StartAbsorb(transform);
    //            hit.collider.isTrigger = true;
    //        }
    //    }
    //}

    void Start()
    {
        // [안전장치] Renderer가 연결되지 않았을 경우 자동 할당
        if (rend == null) rend = GetComponentInChildren<Renderer>();

        originalEmissionColor = DataManager.Instance.initialColor;
        originalFresnelColor = DataManager.Instance.initialColor;

        currentEmissionColor = originalEmissionColor;
        currentFresnelColor = originalFresnelColor;

        rend.material.SetColor("_Emission", currentEmissionColor);
        rend.material.SetColor("_FresnelColor", currentFresnelColor);

        DataManager.Instance.currentColor = currentEmissionColor;
        // ✨ 추가: 처음 시작할 때 목표 색상도 현재 색상으로 초기화
        DataManager.Instance.targetColor = currentEmissionColor;

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

        if (detectedJellies.Length > 0 )
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

            // 데이터 매니저 상태도 원복
            DataManager.Instance.absorbedJellyCount = 0;
            DataManager.Instance.playerCurrentScaleLevel = 1;

            Camera.main.orthographicSize = 6.1f;

            StartCoroutine(DecreaseScale(1.0f, new Vector3(1.0f, 1.0f, 1.0f)));
            currentFadeCoroutine = StartCoroutine(BlendColor(originalEmissionColor, originalFresnelColor, 0.25f));
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        DataManager.Instance.absorbedJellyCount++;
        DataManager.Instance.currentScore += 100;
        jellyCamera.PlayDing();

        // ✨ 1. 기존에 진행 중이던 색상 변화 코루틴이 있다면 즉시 정지
        if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);

        Vector3Int effect = DataManager.Instance.GetJellyEffect(type);

        if (type == JellyColorType.White)
        {
            targetEmissionColor = new Color32(255, 255, 255, 255);
            targetFresnelColor = new Color32(255, 255, 255, 255);
            DataManager.Instance.targetColor = targetEmissionColor;
        }
        else
        {
            // ✨ 2. ApplyJellyColor 내부에서 계산하므로 여기서는 호출만 합니다.
            ApplyJellyColor(new Vector3(effect.x, effect.y, effect.z));
        }

        // ✨ 3. 새 코루틴을 currentFadeCoroutine 변수에 담아서 실행 (중복 방지)
        currentFadeCoroutine = StartCoroutine(BlendColor(targetEmissionColor, targetFresnelColor, 0.5f));

        // 미션 체크
        if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
        {
            DataManager.Instance.missions[1].missionCleared = true;
        }

        // 레벨업 체크
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

            DataManager.Instance.absorbedJellyCount = 0; // 경험치 초기화

            currentStatusUI.ChangeCurrentScaleUI();

            if (mainCamera_Action != null) mainCamera_Action.ScaleChanged();
        }

        Debug.Log($"젤리 흡수: {type}");
    }

    // 반복되는 코드를 줄이기 위한 내부 메서드 예시
    void ApplyJellyColor(Vector3 change)
    {
        // [핵심] 현재 눈에 보이는 색이 아니라, '누적된 목표 색상'에 값을 더해야 동시에 먹어도 중첩됩니다.
        Color32 baseTarget = DataManager.Instance.targetColor;

        targetEmissionColor = baseTarget.AddRGB((int)change.x, (int)change.y, (int)change.z);
        targetFresnelColor = baseTarget.AddRGB((int)change.x, (int)change.y, (int)change.z);

        int darknessStep = DataManager.Instance.darknessStep;
        targetEmissionColor = targetEmissionColor.AddRGB(darknessStep, darknessStep, darknessStep);
        targetFresnelColor = targetFresnelColor.AddRGB(darknessStep, darknessStep, darknessStep);

        // 다음 계산을 위해 매니저의 목표 색상도 갱신
        DataManager.Instance.targetColor = targetEmissionColor;
    }

    IEnumerator BlendColor(Color targetEmission, Color targetFresnel, float time)
    {
        //Color startBase = currentBaseColor;
        Color startEmission = currentEmissionColor;
        Color startFresnel = currentFresnelColor;

        PlayFXAudio.Instance.PlayColorMixSound();

        float t = 0f;

        while (t < time)
        {
            t += Time.deltaTime;
            float progress = t / time;

            //currentBaseColor = Color.Lerp(startBase, targetBase, progress);
            currentEmissionColor = Color.Lerp(startEmission, targetEmission, progress);
            currentFresnelColor = Color.Lerp(startFresnel, targetFresnel, progress);

            //rend.material.SetColor("_BaseColor", currentBaseColor);
            rend.material.SetColor("_Emission", currentEmissionColor);
            rend.material.SetColor("_FresnelColor", currentFresnelColor);

            yield return null;
        }

        // 최종값 강제 설정 (오차 방지)
        //currentBaseColor = targetBase;
        currentEmissionColor = DataManager.Instance.targetColor;
        currentFresnelColor = DataManager.Instance.targetColor;

       // rend.material.SetColor("_BaseColor", currentBaseColor);
        rend.material.SetColor("_Emission", currentEmissionColor);
        rend.material.SetColor("_FresnelColor", currentFresnelColor);

        DataManager.Instance.currentColor = currentEmissionColor;
        currentStatusUI.ChangeCurrentColorUI();
    }

    IEnumerator IncreaseScale(float increaseTime)
    {
        if (softBody3D != null) softBody3D.DisableCloth();

        PlayFXAudio.Instance.PlayScaleUpSound();

        // 배열 범위 초과 방지
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
        currentScale = targetScale; // [중요] 스케일 변수 갱신
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
        currentScale = targetScale; // [중요] 스케일 변수 갱신

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