using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

public enum JellyColorType
{
    Red, Green, Blue,
    Cyan, Magenta, Yellow, White, Black, Temp, None
}

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    //[Header("Color Settings (Enemy)")]
    //public ColorSet[] enemyJellyColorSets; // 기존 에너미용

    //[Header("Color Settings (Player)")]
    //public ColorSet[] playerJellyColorSets; // [추가] 플레이어용

    //[Header("Current Status")]
    //public ColorSet initialColorSet;
    //public ColorSet currentColorSet;
    //public ColorSet targetColorSet;
    //public int currentColorIntensity = 2;
    //public int targetColorIntensity = 2;
    //public int currentColorJellyCount;
    //public int changeColorJellyCount;

    //public List<JellyColorType> jellyBuffer = new List<JellyColorType>();

    [System.Serializable]
    public struct ColorSet
    {
        public string colorName;
        public Material colorMaterial;
        public JellyColorType colorType;
        public Color weak;   // 1단계
        public Color normal; // 2단계
        public Color strong; // 3단계
    }

    // 1. 젤리별 RGB 변화량 데이터 (첫 번째 이미지)
    [System.Serializable]
    public class JellyEffectData
    {
        public string colorName;
        public JellyColorType type;
        public Vector3Int rgbChange; // 기획서의 +30, -15 등을 저장

        //public Vector3Int rgbBaseColor1;
        //public Vector3Int rgbBaseColor2;
        //public Vector3Int rgbFresnelColor;
    }

    [System.Serializable]
    public class ColorRangeRule
    {
        public string colorName;
        public Color color;
        public Vector3 minRGB;
        public JellyColorType resultType;
        public int minPrimary;      // 예: 170 이상
        public int maxOthers;       // 예: 120 이하
        public int primaryMinDifference;   // 예: 다른 색보다 50 높다

        // 복합 색상(노랑, 시안 등)을 위한 추가 필드
        public bool isComposite;     // R+G 같이 두 개를 보는지 여부
        public int minComposite;    // 두 색의 차이 (40 이하)
        public int compositeMaxDifference;
        public int maxOther;
    }

    [Header("Color Rules Settings")]
    [Tooltip("잡색 허용치 (낮을수록 다른 색이 조금만 섞여도 실패)")]
    public int maxImpurity = 40;

    [Tooltip("혼합색 오차 한도 (낮을수록 두 색의 비율이 1:1에 가까워야 함)")]
    public int maxDifference = 15;

    [Header("JellySpawnSettings")]
    public float spawnCoolTime = 10.0f;

    [Header("Scale Settings")]
    public int scaleLevelUpExp = 5;
    public float[] scaleMultiplyPerLevel;
    public int playerCurrentScaleLevel = 1;
    public int absorbedJellyCount = 0;
    public int maxScaleLevel = 5;
    public int targetScaleLevel = 3;
    public float scaleIncreaseTime = 1.0f;

    public float IncreaseJumpForceValue = 5;

    [Header("Camera Settings")]
    public float scaleChangedDuration = 1.0f;
    public float scaleChangedPlusSize = 3.0f;

    public float gameFailPlusSize = -8.0f;

    [Header("Score Settings")]
    public int currentScore = 0;
    public int targetScore = 1000;
    public int scorePerJelly = 100;

    [Header("Detection Settings")]
    public float originalDetectRadius = 4;
    public float detectRadius;
    public float detectPlusRadiusPerLevel = 1.5f;
    public LayerMask detectLayerMask;

    [System.Serializable]
    public struct MissionSet
    {
        public string missionName;
        public bool missionCleared;
    }

    [Header("Mission Settings")]
    public MissionSet[] missions;
    public float targetTime = 60;

    //[Header("Data Access")]
    //public JellyDataDAO jellyDAO;
    //public List<JellyDataDTO> loadedEnemyData;  // 확인용
    //public List<JellyDataDTO> loadedPlayerData; // 확인용 [추가]

    [Header("Color Settings")]
    //public Vector3 redJellyPlusRGB;
    //public Vector3 greenJellyPlusRGB;
    //public Vector3 blueJellyPlusRGB;
    //public Vector3 cyanJellyPlusRGB;
    //public Vector3 magentaJellyPlusRGB;
    //public Vector3 yellowJellyPlusRGB;
    //public Vector3 whiteJellyPlusRGB;

    public Color32 initialViewColor = Color.white;
    public Color32 currentColor;
    public Color32 targetColor;

    public int darknessStep = -20;

    public Color32 initialSystemColor = Color.black;

    [Header("Jelly Effects (Image 1)")]
    public List<JellyEffectData> jellyEffects;

    [Header("Color Range Rules (Image 2)")]
    public List<ColorRangeRule> rangeRules;

    public ColorRangeRule thisGameRangeRule;

    // 특정 젤리 타입의 변화량을 가져오는 함수
    public Vector3Int GetJellyEffect(JellyColorType type)
    {
        var data = jellyEffects.Find(x => x.type == type);
        Debug.Log(data.rgbChange);
        return data != null ? data.rgbChange : Vector3Int.zero;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        int index = UnityEngine.Random.Range(0, rangeRules.Count);
        thisGameRangeRule = rangeRules[index];

        int randint = UnityEngine.Random.Range(0, 2);
        if (randint == 0)
        {
            targetScaleLevel = 4;
        }
        else
        {
            targetScaleLevel = 5;
        }

        InitializeGameData();
        //LoadAndApplyData(); // 데이터 로드 및 적용
    }

    private void InitializeGameData()
    {
        playerCurrentScaleLevel = 1;
        absorbedJellyCount = 0;
        //scaleMultiplyPerLevel = new float[maxLevel];
        currentScore = 0;

        //for (int i = 0; i < scaleMultiplyPerLevel.Length; i++)
        //{
        //    scalePerLevel[i] = 1.5f;
        //}
    }

    // ... (기존 코드들) ...

    public JellyColorType DetermineCurrentColor(Color32 c)
    {
        ColorRangeRule redRule = rangeRules[0];
        ColorRangeRule greenRule = rangeRules[1];
        ColorRangeRule blueRule = rangeRules[2];
        ColorRangeRule cyanRule = rangeRules[3];
        ColorRangeRule magentaRule = rangeRules[4];
        ColorRangeRule yellowRule = rangeRules[5];

        // 1. Red (빨강)
        // 기존 조건 + 잡색(G, B)이 maxImpurity 이하여야 함
        if (c.r >= redRule.minPrimary &&
            (c.r - Mathf.Max(c.g, c.b) >= redRule.primaryMinDifference) &&
            c.g <= maxImpurity && c.b <= maxImpurity)
            return JellyColorType.Red;

        // 2. Green (초록)
        // 기존 조건 + 잡색(R, B)이 maxImpurity 이하여야 함
        if (c.g >= greenRule.minPrimary &&
            (c.g - Mathf.Max(c.r, c.b) >= greenRule.primaryMinDifference) &&
            c.r <= maxImpurity && c.b <= maxImpurity)
            return JellyColorType.Green;

        // 3. Blue (파랑)
        // 기존 조건 + 잡색(R, G)이 maxImpurity 이하여야 함
        if (c.b >= blueRule.minPrimary &&
            (c.b - Mathf.Max(c.r, c.g) >= blueRule.primaryMinDifference) &&
            c.r <= maxImpurity && c.g <= maxImpurity)
            return JellyColorType.Blue;

        // 4. Cyan (시안 = G + B)
        // R이 낮아야 하고, G와 B의 차이가 maxDifference 이하여야 함
        if (c.g >= cyanRule.minComposite && c.b >= cyanRule.minComposite &&
            Mathf.Abs(c.g - c.b) <= maxDifference && c.r <= maxImpurity)
            return JellyColorType.Cyan;

        // 5. Magenta (마젠타 = R + B)
        // G가 낮아야 하고, R과 B의 차이가 maxDifference 이하여야 함
        if (c.r >= magentaRule.minComposite && c.b >= magentaRule.minComposite &&
            Mathf.Abs(c.r - c.b) <= maxDifference && c.g <= maxImpurity)
            return JellyColorType.Magenta;

        // 6. Yellow (노랑 = R + G)
        // B가 낮아야 하고, R과 G의 차이가 maxDifference 이하여야 함
        if (c.r >= yellowRule.minComposite && c.g >= yellowRule.minComposite &&
            Mathf.Abs(c.r - c.g) <= maxDifference && c.b <= maxImpurity)
            return JellyColorType.Yellow;

        // 어떤 조건도 만족하지 못함 (색이 탁하거나 비율이 안 맞음)
        return JellyColorType.None;
    }

    //private void LoadAndApplyData()
    //{
    //    // 1. DAO 초기화
    //    if (jellyDAO == null) jellyDAO = gameObject.AddComponent<JellyDataDAO>();

    //    // 2. CSV 데이터 로드 (DAO 호출)
    //    loadedEnemyData = jellyDAO.LoadJellyData();   // 기존 (에너미)
    //    loadedPlayerData = jellyDAO.LoadPlayerData(); // 신규 (플레이어)

    //    // 3. 데이터를 ColorSet으로 변환하여 적용
    //    // 에너미 젤리 적용
    //    if (loadedEnemyData != null)
    //    {
    //        enemyJellyColorSets = ConvertDtosToColorSets(loadedEnemyData);
    //    }

    //    // 플레이어 젤리 적용 [추가]
    //    if (loadedPlayerData != null)
    //    {
    //        playerJellyColorSets = ConvertDtosToColorSets(loadedPlayerData);
    //    }
    //    else
    //    {
    //        Debug.LogWarning("플레이어 데이터가 로드되지 않았습니다. (Resources/Data/PlayerData.csv 확인 필요)");
    //    }

    //    // 4. 초기값 설정 (에너미 데이터 기준 예시, 필요시 플레이어로 변경 가능)
    //    if (playerJellyColorSets != null && playerJellyColorSets.Length > 0)
    //    {
    //        initialColorSet = playerJellyColorSets[0];
    //        currentColorSet = initialColorSet;
    //        targetColorSet = initialColorSet;
    //    }

    //    Debug.Log($"데이터 로드 완료 - Enemy: {playerJellyColorSets?.Length ?? 0}개, Player: {playerJellyColorSets?.Length ?? 0}개");
    //}

    //// [핵심] DTO 리스트를 ColorSet 배열로 변환하는 공통 함수
    //private ColorSet[] ConvertDtosToColorSets(List<JellyDataDTO> dataList)
    //{
    //    if (dataList == null || dataList.Count == 0) return new ColorSet[0];

    //    // 1. 그룹화
    //    Dictionary<JellyColorType, List<JellyDataDTO>> groupedData = new Dictionary<JellyColorType, List<JellyDataDTO>>();
    //    foreach (var dto in dataList)
    //    {
    //        if (!groupedData.ContainsKey(dto.ColorType))
    //        {
    //            groupedData[dto.ColorType] = new List<JellyDataDTO>();
    //        }
    //        groupedData[dto.ColorType].Add(dto);
    //    }

    //    // 2. ColorSet 생성
    //    List<ColorSet> resultColorSets = new List<ColorSet>();

    //    foreach (var group in groupedData)
    //    {
    //        JellyColorType type = group.Key;
    //        List<JellyDataDTO> dtos = group.Value;

    //        ColorSet set = new ColorSet();
    //        set.colorType = type;
    //        set.colorName = type.ToString();

    //        // 강도별 색상 할당
    //        JellyDataDTO weakDto = dtos.Find(d => d.ColorIntensity == 1);
    //        if (weakDto != null) set.weak = weakDto.GetColor();

    //        JellyDataDTO normalDto = dtos.Find(d => d.ColorIntensity == 2);
    //        if (normalDto != null) set.normal = normalDto.GetColor();

    //        JellyDataDTO strongDto = dtos.Find(d => d.ColorIntensity == 3);
    //        if (strongDto != null) set.strong = strongDto.GetColor();

    //        // 머티리얼 로드
    //        JellyDataDTO representativeDto = normalDto ?? weakDto ?? strongDto;
    //        if (representativeDto != null)
    //        {
    //            // 경로 예시: Models/BearJelly/Materials/MaterialName
    //            // 주의: 플레이어와 에너미의 머티리얼 폴더 경로가 다르다면 DTO에 경로 필드를 추가하거나 여기서 분기 처리가 필요할 수 있음.
    //            // 현재는 CSV의 'MaterialPath' 값을 그대로 사용한다고 가정.
    //            string path = $"Models/BearJelly/Materials/{representativeDto.MaterialPath}";
    //            set.colorMaterial = Resources.Load<Material>(path);

    //            if (set.colorMaterial == null)
    //            {
    //                // 혹시 경로가 다를 수 있으니 로그만 띄움
    //                // Debug.LogWarning($"머티리얼 로드 실패: {path}");
    //            }
    //        }

    //        resultColorSets.Add(set);
    //    }

    //    return resultColorSets.ToArray();
    //}
}