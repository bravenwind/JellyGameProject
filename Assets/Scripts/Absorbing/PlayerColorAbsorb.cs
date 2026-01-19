using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerColorAbsorb : MonoBehaviour
{
    public Renderer rend;

    // 색상 변수들
    private Color originalBaseColor;
    private Color originalEmissionColor;
    private Color originalSSSColor;
    private Color originalFresnelColor;

    private Color currentBaseColor;
    private Color currentEmissionColor;
    private Color currentSSSColor;
    private Color currentFresnelColor;

    private Color targetBaseColor;
    private Color targetEmissionColor;
    private Color targetSSSColor;
    private Color targetFresnelColor;

    public Rigidbody[] rigidbodies;
    public ColorUI colorUI;

    private Vector3 currentScale;
    public SoftBody3D softBody3D;

    public Cloth playerCloth;
    public LevelUI levelUI;
    public ScoreUI scoreUI;
    public MissionUI missionUI;
    public JellyCamera jellyCamera;

    private MainCamera_Action mainCamera_Action;
    private Coroutine currentFadeCoroutine;

    void Start()
    {
        // [안전장치] Renderer가 연결되지 않았을 경우 자동 할당
        if (rend == null) rend = GetComponentInChildren<Renderer>();

        originalBaseColor = rend.material.GetColor("_BaseColor");
        originalEmissionColor = rend.material.GetColor("_Emission");
        originalSSSColor = rend.material.GetColor("_SSSColor");
        originalFresnelColor = rend.material.GetColor("_FresnelColor");

        currentBaseColor = originalBaseColor;
        currentEmissionColor = originalEmissionColor;
        currentSSSColor = originalSSSColor;
        currentFresnelColor = originalFresnelColor;

        colorUI.ChangeColorUI_CurrentJelly();
        colorUI.ChangeColorUI_TargetColor(new Color(1,1,1,0));
        currentScale = transform.localScale;
        playerCloth = GetComponentInChildren<Cloth>();

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
            DataManager.Instance.targetColorSet = DataManager.Instance.initialColorSet;
            DataManager.Instance.currentColorSet = DataManager.Instance.initialColorSet;
            DataManager.Instance.absorbedJellyCount = 0;
            DataManager.Instance.playerCurrentLevel = 1;

            Camera.main.orthographicSize = 6.1f;

            StartCoroutine(DecreaseScale(1.0f, new Vector3(1.0f, 1.0f, 1.0f)));
            currentFadeCoroutine = StartCoroutine(BlendColor2(originalBaseColor, originalEmissionColor, originalSSSColor, originalFresnelColor, 0.25f));
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        DataManager.Instance.absorbedJellyCount++;
        jellyCamera.PlayDing();

        if (type == JellyColorType.Blue)
        {
            DataManager.Instance.missions[0].missionCleared = true;
        }

        if(type == JellyColorType.Black)
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[7];
            Debug.Log("검정 먹음");
            UpdatePlayerVisual(true);
            return;
        }

        if (DataManager.Instance.currentColorJellyCount < 3)
        {
            DataManager.Instance.currentColorJellyCount++;
        }

        if (CheckIfJellyCanBeAdded(DataManager.Instance.currentColorJellyCount - 1))
        {
            AddJellyToBuffer(type, DataManager.Instance.currentColorJellyCount - 1);
        }
        else
        {
            DataManager.Instance.jellyBuffer[0] = DataManager.Instance.jellyBuffer[1];
            DataManager.Instance.jellyBuffer[1] = DataManager.Instance.jellyBuffer[2];
            DataManager.Instance.jellyBuffer[2] = type;
        }

        if (CheckIfThereIsNoTemp())
        {
            if (DataManager.Instance.jellyBuffer[0] == DataManager.Instance.jellyBuffer[1]
                && DataManager.Instance.jellyBuffer[1] == DataManager.Instance.jellyBuffer[2])
            {
                ProcessColorChange();
            }
            else
            {
                DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[7];
                if (colorUI) colorUI.ChangeColorUI_TargetColor(CheckColorIntensity());
            }
        }

        DataManager.Instance.currentScore += 100;

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
                StartCoroutine(IncreaseScale(0.5f));
            }
            if (mainCamera_Action != null) mainCamera_Action.ScaleChanged();

            DataManager.Instance.playerCurrentLevel++;
            DataManager.Instance.absorbedJellyCount = 0; // 경험치 초기화
        }

        // UI 갱신 (Null 체크 권장)
        if (levelUI) levelUI.ChangeLevelUI();
        if (scoreUI) scoreUI.ChangeScoreUI();
        if (missionUI) missionUI.ChangeMissionUI();
        if (colorUI) colorUI.ChangeColorUI_CurrentJelly();

        Debug.Log($"젤리 흡수: {type}");
    }

    void AddJellyToBuffer(JellyColorType newJelly, int index)
    {
        DataManager.Instance.jellyBuffer[index] = newJelly;
    }

    void ProcessColorChange()
    {
        // 1. 같은 색 계열인지 확인 (농도 증가)
        if (IsAllSameColor())
        {
            DataManager.Instance.targetColorIntensity = Mathf.Min(DataManager.Instance.currentColorIntensity + 1, 3);
            // 같은 색일 때는 타겟 컬러셋 변경 없음 (현재 유지)
            DataManager.Instance.targetColorSet = DataManager.Instance.currentColorSet;

            Debug.Log("빨강 더 센 단계로");
        }
        // 2. 흰색 젤리인지 확인 (농도 감소)
        else if (IsAllWhite())
        {
            if (DataManager.Instance.currentColorSet.colorType == JellyColorType.Black)
            {
                DataManager.Instance.targetColorSet = DataManager.Instance.initialColorSet;
                DataManager.Instance.targetColorIntensity = 2;
            }
            else
            {
                DataManager.Instance.targetColorIntensity = Mathf.Max(DataManager.Instance.currentColorIntensity - 1, 0);
                // 흰색일 때도 컬러셋 자체는 유지하고 농도만 줄이는 것인지 확인 필요. 일단 유지.
                DataManager.Instance.targetColorSet = DataManager.Instance.currentColorSet;
            }
        }
        else if (!CombineColor())
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[7];
        }

        if (colorUI) colorUI.ChangeColorUI_TargetColor(CheckColorIntensity());
        //UpdatePlayerVisual();
    }

    bool IsAllSameColor()
    {
        // 버퍼에 있는 젤리 색상들이 현재 플레이어 색상과 같은지 확인
        foreach (JellyColorType jellyColor in DataManager.Instance.jellyBuffer)
        {
            if (jellyColor != DataManager.Instance.currentColorSet.colorType)
            {
                return false;
            }
        }
        return true;
    }

    bool IsAllWhite()
    {
        foreach (JellyColorType jellyColor in DataManager.Instance.jellyBuffer)
        {
            if (jellyColor != JellyColorType.White) return false;
        }
        return true;
    }

    bool IsAllOtherColor()
    {
        // 버퍼에 있는 젤리 색상들이 현재 플레이어 색상과 같은지 확인
        foreach (JellyColorType jellyColor in DataManager.Instance.jellyBuffer)
        {
            if (jellyColor == DataManager.Instance.currentColorSet.colorType)
            {
                return false;
            }
        }
        return true;
    }

    bool CombineColor()
    {
        // DataManager의 jellyBuffer[0,1,2]는 이미 위에서 모두 같은 색임이 보장됨 (AddJellyToBuffer 조건 때문)
        // 따라서 jellyBuffer[0]만 확인해도 됨.
        JellyColorType absorbedColor = DataManager.Instance.jellyBuffer[0];
        JellyColorType currentColor = DataManager.Instance.currentColorSet.colorType;
        bool isCombined = false;
        
        // [수정됨] 조합 로직 가독성 개선 및 인덱스 하드코딩 주의
        // 1. Red + Green = Yellow (Index 5 가정)
        if ((currentColor == JellyColorType.Red && absorbedColor == JellyColorType.Green)
            || (currentColor == JellyColorType.Green && absorbedColor == JellyColorType.Red))
        {
            DataManager.ColorSet yellowColorSet = DataManager.Instance.jellyColorSets[5];
            DataManager.Instance.targetColorSet = yellowColorSet; // Yellow
            isCombined = true;
        }
        // 2. Green + Blue = Cyan (Index 3 가정)
        else if ((currentColor == JellyColorType.Green && absorbedColor == JellyColorType.Blue)
            || (currentColor == JellyColorType.Blue && absorbedColor == JellyColorType.Green))
        {
            DataManager.ColorSet cyanColorSet = DataManager.Instance.jellyColorSets[3];
            DataManager.Instance.targetColorSet = cyanColorSet; // Cyan
            isCombined = true;
        }
        // 3. Blue + Red = Magenta (Index 4 가정)
        else if ((currentColor == JellyColorType.Blue && absorbedColor == JellyColorType.Red)
            || (currentColor == JellyColorType.Red && absorbedColor == JellyColorType.Blue))
        {
            DataManager.ColorSet magentaColorSet = DataManager.Instance.jellyColorSets[4];
            DataManager.Instance.targetColorSet = magentaColorSet; // Magenta
            isCombined = true;
        }
        else if ((currentColor == JellyColorType.Yellow && absorbedColor == JellyColorType.Red))
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[0]; // Red
            isCombined = true;
        }
        else if ((currentColor == JellyColorType.Yellow && absorbedColor == JellyColorType.Green))
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[1]; // Green
            isCombined = true;
        }
        else if ((currentColor == JellyColorType.Magenta && absorbedColor == JellyColorType.Red))
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[0]; // Red
            isCombined = true;
        }
        else if ((currentColor == JellyColorType.Magenta && absorbedColor == JellyColorType.Blue))
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[2]; // Blue
            isCombined = true;
        }
        else if ((currentColor == JellyColorType.Cyan && absorbedColor == JellyColorType.Green))
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[1]; // Green
            isCombined = true;
        }
        else if ((currentColor == JellyColorType.Cyan && absorbedColor == JellyColorType.Blue))
        {
            DataManager.Instance.targetColorSet = DataManager.Instance.jellyColorSets[2]; // Blue
            isCombined = true;
        }

        DataManager.Instance.targetColorIntensity = 2;
        return isCombined;
    }

    Color CheckColorIntensity()
    {
        // [수정됨] targetColorSet을 기준으로 가져와야 함 (DataManager의 targetIntensity 사용)
        DataManager.ColorSet targetSet = DataManager.Instance.targetColorSet;
        int intensity = DataManager.Instance.targetColorIntensity;

        if (intensity == 1) return targetSet.weak;
        if (intensity == 3) return targetSet.strong;
        return targetSet.normal; // 기본값 2
    }

    public void UpdatePlayerVisual(bool isBlack = false)
    {
        if (!isBlack)
        {
            if (DataManager.Instance.currentColorJellyCount < 3)
            {
                return;
            }
        }
        
        Material targetColorMaterial = DataManager.Instance.targetColorSet.colorMaterial;

        targetBaseColor = targetColorMaterial.GetColor("_BaseColor");
        targetEmissionColor = CheckColorIntensity();
        targetSSSColor = targetColorMaterial.GetColor("_SSSColor");
        targetFresnelColor = targetColorMaterial.GetColor("_FresnelColor");

        // [수정됨] 기존 코루틴이 돌고 있다면 반드시 중지해야 꼬이지 않음
        if (currentFadeCoroutine != null) StopCoroutine(currentFadeCoroutine);
        currentFadeCoroutine = StartCoroutine(BlendColor2(targetBaseColor, targetEmissionColor, targetSSSColor, targetFresnelColor, 1.0f));

        // [수정됨] 시각적 업데이트 시작 시, 논리적 데이터(Current)도 Target으로 갱신해줘야 함!
        // 그래야 다음 흡수 때 바뀐 색을 기준으로 판정함
        DataManager.Instance.currentColorSet = DataManager.Instance.targetColorSet;
        DataManager.Instance.currentColorIntensity = DataManager.Instance.targetColorIntensity;
        DataManager.Instance.currentColorJellyCount = 0;

        for (int i = 0; i < DataManager.Instance.jellyBuffer.Count; i++)
        {
            DataManager.Instance.jellyBuffer[i] = JellyColorType.Temp;
        }

        if (colorUI) colorUI.ChangeColorUI_CurrentJelly();
        if (colorUI) colorUI.ChangeColorUI_TargetColor(new Color(1,1,1,0));
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

            rend.material.SetColor("_BaseColor", currentBaseColor);
            rend.material.SetColor("_Emission", currentEmissionColor);
            rend.material.SetColor("_SSSColor", currentSSSColor);
            rend.material.SetColor("_FresnelColor", currentFresnelColor);

            yield return null;
        }

        // 최종값 강제 설정 (오차 방지)
        currentBaseColor = targetBase;
        currentEmissionColor = targetEmission;
        currentSSSColor = targetSSS;
        currentFresnelColor = targetFresnel;

        rend.material.SetColor("_BaseColor", currentBaseColor);
        rend.material.SetColor("_Emission", currentEmissionColor);
        rend.material.SetColor("_SSSColor", currentSSSColor);
        rend.material.SetColor("_FresnelColor", currentFresnelColor);

        DataManager.Instance.currentColor = currentEmissionColor;
    }

    IEnumerator IncreaseScale(float increaseTime)
    {
        if (softBody3D != null) softBody3D.DisableCloth();

        Vector3 startScale = currentScale;
        // 배열 범위 초과 방지
        int levelIndex = Mathf.Clamp(DataManager.Instance.playerCurrentLevel - 1, 0, DataManager.Instance.addScalePerLevel.Length - 1);
        Vector3 targetScale = startScale * DataManager.Instance.addScalePerLevel[levelIndex];

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

    bool CheckIfJellyCanBeAdded(int index)
    {
        if (DataManager.Instance.jellyBuffer[index] == JellyColorType.Temp)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    bool CheckIfThereIsNoTemp()
    {
        return !DataManager.Instance.jellyBuffer.Contains(JellyColorType.Temp);
    }
}