using System.Collections;
using System.Collections.Generic;
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
    public ColorUI colorUI;

    private MainCamera_Action mainCamera_Action;
    private Coroutine currentFadeCoroutine;

    //public UIFollowTarget followTarget;
    public UIFollowTarget scaleIncreasedEffect;
    public UIPoolManager uIPoolManager;
    public PlayerController playerController;

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

        originalScale = Vector3.one;
        currentScale = transform.localScale;
        playerCloth = GetComponentInChildren<Cloth>();
        colorUI.ChangeCurrentColorUI();

        if (Camera.main != null)
            mainCamera_Action = Camera.main.gameObject.GetComponent<MainCamera_Action>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Object"))
        {
            foreach (Rigidbody rigidbody in rigidbodies)
            {
                rigidbody.constraints = RigidbodyConstraints.None;
            }
        }
    }

    private void Update()
    {
        // 디버그용 리셋
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("원상복구 시도");
            if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);

            // 데이터 매니저 상태도 원복
            DataManager.Instance.absorbedJellyCount = 0;
            DataManager.Instance.playerCurrentLevel = 1;

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

        // 데이터 매니저에서 변화량 가져오기
        Vector3Int effect = DataManager.Instance.GetJellyEffect(type);

        Debug.Log(effect.ToString());

        if (type == JellyColorType.White)
        {
            targetEmissionColor = new Color32(255, 255, 255, 255);
            targetFresnelColor = new Color32(255, 255, 255, 255);
        }
        else
        {
            // 리스트에서 찾은 값으로 적용
            ApplyJellyColor(new Vector3(effect.x, effect.y, effect.z));
        }

        StartCoroutine(BlendColor(targetEmissionColor, targetFresnelColor, 0.5f));

        // 미션 체크
        if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
        {
            DataManager.Instance.missions[1].missionCleared = true;
        }

        // 레벨업 체크
        if (DataManager.Instance.absorbedJellyCount >= DataManager.Instance.levelUpExp)
        {

            if (DataManager.Instance.playerCurrentLevel < DataManager.Instance.maxLevel)
            {
                uIPoolManager.SpawnUI(scaleIncreasedEffect, transform);
                StartCoroutine(IncreaseScale(0.5f));
            }

            DataManager.Instance.playerCurrentLevel++;
            DataManager.Instance.absorbedJellyCount = 0; // 경험치 초기화

            if (mainCamera_Action != null) mainCamera_Action.ScaleChanged();
        }

        Debug.Log($"젤리 흡수: {type}");
    }

    // 반복되는 코드를 줄이기 위한 내부 메서드 예시
    void ApplyJellyColor(Vector3 change)
    {
        // 정수 기반 계산을 위해 (int) 캐스팅
        targetEmissionColor = currentEmissionColor.AddRGB((int)change.x, (int)change.y, (int)change.z);
        targetFresnelColor = currentFresnelColor.AddRGB((int)change.x, (int)change.y, (int)change.z);

        // [중요] '많이 먹으면 검은색' 기획을 위해 모든 젤리 섭취 시 전체 명도를 살짝 깎음
        // 이 줄이 있어야 흰색(255)에서 시작해도 결국 0(검정)으로 수렴합니다.
        int darknessStep = DataManager.Instance.darknessStep;
        targetEmissionColor = targetEmissionColor.AddRGB(darknessStep, darknessStep, darknessStep);
        targetFresnelColor = targetFresnelColor.AddRGB(darknessStep, darknessStep, darknessStep);
    }

    IEnumerator BlendColor(Color targetEmission, Color targetFresnel, float time)
    {
        //Color startBase = currentBaseColor;
        Color startEmission = currentEmissionColor;
        Color startFresnel = currentFresnelColor;

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
        currentEmissionColor = targetEmission;
        currentFresnelColor = targetFresnel;

       // rend.material.SetColor("_BaseColor", currentBaseColor);
        rend.material.SetColor("_Emission", currentEmissionColor);
        rend.material.SetColor("_FresnelColor", currentFresnelColor);

        DataManager.Instance.currentColor = currentEmissionColor;
        colorUI.ChangeCurrentColorUI();
    }

    IEnumerator IncreaseScale(float increaseTime)
    {
        if (softBody3D != null) softBody3D.DisableCloth();
        // 배열 범위 초과 방지
        Vector3 startScale = currentScale;
        int levelIndex = Mathf.Clamp(DataManager.Instance.playerCurrentLevel - 1, 0, DataManager.Instance.maxLevel - 1);
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
}