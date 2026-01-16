using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerColorAbsorb : MonoBehaviour
{
    public Renderer rend;
    public Color originalBaseColor;
    public Color originalEmissionColor;
    public Color originalSSSColor;
    public Color originalFresnelColor;

    private Color32 currentBaseColor;
    private Color32 currentEmissionColor;
    private Color32 currentSSSColor;
    private Color32 currentFresnelColor;
    public Rigidbody[] rigidbodies;
    public ColorUI colorUI;

    private Vector3 currentScale;
    public SoftBody3D softBody3D;

    // SoftBody3D 스크립트나 Cloth 컴포넌트를 가져오기 위한 변수
    public Cloth playerCloth;
    public LevelUI levelUI;
    public ScoreUI scoreUI;
    public MissionUI missionUI;

    private MainCamera_Action mainCamera_Action;
    private Coroutine currentFadeCoroutine; 

    void Start()
    {
        originalBaseColor = rend.material.GetColor("_BaseColor");
        originalEmissionColor = rend.material.GetColor("_Emission");
        originalSSSColor = rend.material.GetColor("_SSSColor");
        originalFresnelColor = rend.material.GetColor("_FresnelColor");

        currentBaseColor = originalBaseColor;
        currentEmissionColor = originalEmissionColor;
        currentSSSColor = originalSSSColor;
        currentFresnelColor = originalFresnelColor;

        colorUI.ChangeColor(new Color(currentEmissionColor.r, currentEmissionColor.g, currentEmissionColor.b));
        currentScale = transform.localScale;
        playerCloth = GetComponentInChildren<Cloth>();
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
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("원상복구 시도");

            // 1. 기존에 색을 바꾸던 코루틴이 있다면 강제 중단
            if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);

            // 2. 새로운 코루틴 시작 (변수에 저장)
            currentFadeCoroutine = StartCoroutine(BlendColor2(originalBaseColor, originalEmissionColor, originalSSSColor, originalFresnelColor, 0.25f)); // 시간도 0.25초로 통일 추천
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        Color32 targetColor = currentEmissionColor;
        Color targetBaseColor = currentBaseColor;
        Color targetEmissionColor = currentEmissionColor;
        Color targetSSSColor = currentSSSColor;
        Color targetFresnelColor = currentFresnelColor;

        Debug.Log("흡수함");
        //switch (type)
        //{
        //    case JellyColorType.Red:
        //        DataManager.Instance.eatenRedJellyCount++;
        //        targetColor.r = (byte)Mathf.Clamp(targetColor.r + DataManager.Instance.redPlusColor.x, 0, 255);
        //        break;

        //    case JellyColorType.Green:
        //        DataManager.Instance.eatenGreenJellyCount++;
        //        targetColor.g = (byte)Mathf.Clamp(targetColor.g + DataManager.Instance.greenPlusColor.y, 0, 255);
        //        break;

        //    case JellyColorType.Blue:
        //        DataManager.Instance.missions[0].missionCleared = true;
        //        DataManager.Instance.eatenBlueJellyCount++;
        //        targetColor.b = (byte)Mathf.Clamp(targetColor.b + DataManager.Instance.bluePlusColor.z, 0, 255);
        //        break;
        //    case JellyColorType.Black:
        //        targetColor = Color.black;
        //        break;
        //}

        //Vector3의 x, y, z 값을 각각 r, g, b에 더해줍니다.
        switch (type)
        {
            case JellyColorType.Red:
                targetColor.r = (byte)Mathf.Clamp(targetColor.r + DataManager.Instance.redPlusColor.x, 0, 255);
                break;

            case JellyColorType.Green:
                targetColor.g = (byte)Mathf.Clamp(targetColor.g + DataManager.Instance.greenPlusColor.y, 0, 255);
                break;

            case JellyColorType.Blue:
                targetColor.b = (byte)Mathf.Clamp(targetColor.b + DataManager.Instance.bluePlusColor.z, 0, 255);
                DataManager.Instance.missions[0].missionCleared = true;
                break;

            case JellyColorType.Yellow:
                targetColor.r = (byte)Mathf.Clamp(targetColor.r + DataManager.Instance.yellowPlusColor.x, 0, 255);
                targetColor.g = (byte)Mathf.Clamp(targetColor.g + DataManager.Instance.yellowPlusColor.y, 0, 255);
                break;

            case JellyColorType.Magenta:
                // Magenta는 Red(X) + Blue(Z)
                targetColor.r = (byte)Mathf.Clamp(targetColor.r + DataManager.Instance.magentaPlusColor.x, 0, 255);
                targetColor.b = (byte)Mathf.Clamp(targetColor.b + DataManager.Instance.magentaPlusColor.z, 0, 255);
                break;

            case JellyColorType.Cyan:
                // Cyan은 Green(Y) + Blue(Z)
                targetColor.g = (byte)Mathf.Clamp(targetColor.g + DataManager.Instance.cyanPlusColor.y, 0, 255);
                targetColor.b = (byte)Mathf.Clamp(targetColor.b + DataManager.Instance.cyanPlusColor.z, 0, 255);
                break;

            case JellyColorType.White:
                Material whiteMaterial = DataManager.Instance.playerColors[6].colorMaterial;
                targetBaseColor = whiteMaterial.GetColor("_BaseColor");
                targetEmissionColor = whiteMaterial.GetColor("_Emission");
                targetSSSColor = whiteMaterial.GetColor("_SSSColor");
                targetFresnelColor = whiteMaterial.GetColor("_FresnelColor");
                break;
            case JellyColorType.Black:
                Material blackMaterial = DataManager.Instance.playerColors[7].colorMaterial;
                targetBaseColor = blackMaterial.GetColor("_BaseColor");
                targetEmissionColor = blackMaterial.GetColor("_Emission");
                targetSSSColor = blackMaterial.GetColor("_SSSColor");
                targetFresnelColor = blackMaterial.GetColor("_FresnelColor");
                break;
        }

        targetColor.a = 255;

        DataManager.Instance.absorbedJellyCount++;
        DataManager.Instance.currentScore += 100;

        if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
        {
            DataManager.Instance.missions[1].missionCleared = true;
        }

        if (DataManager.Instance.absorbedJellyCount == DataManager.Instance.levelUpExp)
        {
            StartCoroutine(IncreaseScale(0.5f));
            mainCamera_Action.ScaleChanged();
            DataManager.Instance.playerCurrentLevel++;
            DataManager.Instance.absorbedJellyCount = 0;
        }

        levelUI.ChangeLevelUI();
        scoreUI.ChangeScoreUI();
        missionUI.ChangeMissionUI();

        if (DataManager.Instance.eatenRedJellyCount > DataManager.Instance.changeColorJellyCount)
        {

        }

        Debug.Log($"{targetColor.r}, {targetColor.g}, {targetColor.b}, {targetColor.a}");
        if (type == JellyColorType.Black || type == JellyColorType.White)
        {
            // 실행된 코루틴을 변수에 저장
            currentFadeCoroutine = StartCoroutine(BlendColor2(targetBaseColor, targetEmissionColor, targetSSSColor, targetFresnelColor, 0.25f));
            return;
        }

        // 실행된 코루틴을 변수에 저장
        currentFadeCoroutine = StartCoroutine(BlendColor(targetColor, 0.25f));
    }

    IEnumerator BlendColor(Color targetEmission, float time)
    {
        Color32 startEmission = currentEmissionColor;
        float t = 0f;

        while (t < time)
        {
            t += Time.deltaTime;
            float progress = t / time;

            currentEmissionColor = Color32.Lerp(startEmission, targetEmission, progress);

            //rend.material.color = currentColor;
            rend.material.SetColor("_Emission", currentEmissionColor);

            yield return null;
        }

        currentEmissionColor = targetEmission;

        //rend.material.color = currentColor;
        rend.material.SetColor("_Emission", currentEmissionColor);
        DataManager.Instance.currentColor = currentEmissionColor;

        //// 각 채널별 차이의 절댓값(Abs) 계산
        //int diffR = Mathf.Abs(DataManager.Instance.currentColor.r - DataManager.Instance.targetColor.r);
        //int diffG = Mathf.Abs(DataManager.Instance.currentColor.g - DataManager.Instance.targetColor.g);
        //int diffB = Mathf.Abs(DataManager.Instance.currentColor.b - DataManager.Instance.targetColor.b);

        //// R, G, B 모두 차이가 30 이하라면 로그 출력
        //if (diffR <= 30 && diffG <= 30 && diffB <= 30)
        //{
        //    Debug.Log($"<color=cyan>Target Color Reached!</color> | Current: {DataManager.Instance.currentColor} / Target: {DataManager.Instance.targetColor}");
        //    Debug.Log($"Difference - R: {diffR}, G: {diffG}, B: {diffB}");

        //    // 여기에 목표 달성 시 실행할 코드를 넣으시면 됩니다.
        //}
        colorUI.ChangeColor(currentEmissionColor);
    }

    IEnumerator BlendColor2(Color targetBase, Color targetEmission, Color targetSSS, Color targetFresnel, float time)
    {
        Color startEmission = currentEmissionColor;
        Color startBase = currentBaseColor;
        Color startSSS = currentSSSColor;
        Color startFresnel = currentFresnelColor;

        float t = 0f;

        while (t < time)
        {
            t += Time.deltaTime;
            float progress = t / time;

            currentBaseColor = Color.Lerp(startBase, targetBase, progress);
            currentEmissionColor = Color.Lerp(startEmission, targetEmission, progress);
            currentSSSColor = Color.Lerp(startSSS, targetSSS, progress);
            currentFresnelColor = Color.Lerp(startFresnel, targetFresnel, progress);

            //rend.material.color = currentColor;
            rend.material.SetColor("_BaseColor", currentBaseColor);
            rend.material.SetColor("_Emission", currentEmissionColor);
            rend.material.SetColor("_SSSColor", currentSSSColor);
            rend.material.SetColor("_FresnelColor", currentFresnelColor);

            yield return null;
        }

        currentEmissionColor = targetEmission;
        currentBaseColor = targetBase;
        currentSSSColor = targetSSS;
        currentFresnelColor = targetFresnel;

        //rend.material.color = currentColor;
        rend.material.SetColor("_BaseColor", currentBaseColor);
        rend.material.SetColor("_Emission", currentEmissionColor);
        rend.material.SetColor("_SSSColor", currentSSSColor);
        rend.material.SetColor("_FresnelColor", currentFresnelColor);

        DataManager.Instance.currentColor = currentEmissionColor;
        colorUI.ChangeColor(currentEmissionColor);
    }

    IEnumerator IncreaseScale(float increaseTime)
    {
        // 🔥 Cloth 완전히 정지
        if (softBody3D != null)
            softBody3D.DisableCloth();

        Vector3 startScale = currentScale;
        Vector3 targetScale = startScale * DataManager.Instance.addScalePerLevel[DataManager.Instance.playerCurrentLevel - 1];

        float t = 0f;

        while (t < increaseTime)
        {
            t += Time.deltaTime;
            float progress = t / increaseTime;

            currentScale = Vector3.Lerp(startScale, targetScale, progress);
            transform.localScale = currentScale;

            yield return null;
        }

        transform.localScale = currentScale;

        // 🔥 Cloth 재초기화
        if (softBody3D != null)
        {
            // SoftBody3D의 코루틴을 실행합니다.
            StartCoroutine(softBody3D.EnableAndRebuildCloth());
        }
    }
}
