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
    }

    [System.Serializable]
    public class ColorRangeRule
    {
        public string colorName;
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

    [Header("Scale Settings")]
    public int levelUpExp = 5;
    public float[] scaleMultiplyPerLevel;
    public int playerCurrentLevel = 1;
    public int absorbedJellyCount = 0;
    public int maxLevel = 5;

    [Header("Camera Settings")]
    public float scaleChangedDuration = 1.0f;
    public float scaleChangedPlusSize = 3.0f;

    [Header("Score Settings")]
    public int currentScore = 0;
    public int targetScore = 1000;
    public int scorePerJelly = 100;

    [System.Serializable]
    public struct MissionSet
    {
        public string missionName;
        public bool missionCleared;
    }

    [Header("Mission Settings")]
    public MissionSet[] missions;

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

    public Color32 initialColor;
    public Color32 currentColor;
    public Color32 targetColor;

    public int darknessStep = -20;

    [Header("Jelly Effects (Image 1)")]
    public List<JellyEffectData> jellyEffects;

    [Header("Color Range Rules (Image 2)")]
    public List<ColorRangeRule> rangeRules;

    // 특정 젤리 타입의 변화량을 가져오는 함수
    public Vector3Int GetJellyEffect(JellyColorType type)
    {
        var data = jellyEffects.Find(x => x.type == type);
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

        InitializeGameData();
        //LoadAndApplyData(); // 데이터 로드 및 적용
    }

    private void InitializeGameData()
    {
        playerCurrentLevel = 1;
        absorbedJellyCount = 0;
        //scaleMultiplyPerLevel = new float[maxLevel];
        currentScore = 0;

        //for (int i = 0; i < scaleMultiplyPerLevel.Length; i++)
        //{
        //    scalePerLevel[i] = 1.5f;
        //}
    }

    public JellyColorType DetermineCurrentColor(Color32 c)
    {
        // 리스트에서 각 색상의 규칙을 가져옴 (RGBCMY 순서)
        ColorRangeRule redRule = DataManager.Instance.rangeRules[0];
        ColorRangeRule greenRule = DataManager.Instance.rangeRules[1];
        ColorRangeRule blueRule = DataManager.Instance.rangeRules[2];
        ColorRangeRule cyanRule = DataManager.Instance.rangeRules[3];
        ColorRangeRule magentaRule = DataManager.Instance.rangeRules[4];
        ColorRangeRule yellowRule = DataManager.Instance.rangeRules[5];

        // 1. Red (빨강)
        // 기준: 빨강 170 이상, 파랑/초록 120 이하, 빨강이 다른 색보다 50 높음
        if (c.r >= redRule.minPrimary && c.g <= redRule.maxOthers && c.b <= redRule.maxOthers &&
            (c.r - c.g >= redRule.primaryMinDifference) && (c.r - c.b >= redRule.primaryMinDifference))
            return JellyColorType.Red;

        // 2. Green (초록)
        // 기준: 초록 170 이상, 파랑/빨강 120 이하, 초록이 다른 색보다 50 높음
        if (c.g >= greenRule.minPrimary && c.r <= greenRule.maxOthers && c.b <= greenRule.maxOthers &&
            (c.g - c.r >= greenRule.primaryMinDifference) && (c.g - c.b >= greenRule.primaryMinDifference))
            return JellyColorType.Green;

        // 3. Blue (파랑)
        // 기준: 파랑 170 이상, 빨강/초록 120 이하, 파랑이 다른 색보다 50 높음
        if (c.b >= blueRule.minPrimary && c.r <= blueRule.maxOthers && c.g <= blueRule.maxOthers &&
            (c.b - c.r >= blueRule.primaryMinDifference) && (c.b - c.g >= blueRule.primaryMinDifference))
            return JellyColorType.Blue;

        // 4. Cyan (시안)
        // 기준: 파랑과 초록 150 이상, 파랑과 초록 차이 40 이하, 빨강 120 이하
        if (c.g >= cyanRule.minComposite && c.b >= cyanRule.minComposite &&
            Mathf.Abs(c.g - c.b) <= cyanRule.compositeMaxDifference && c.r <= cyanRule.maxOther)
            return JellyColorType.Cyan;

        // 5. Magenta (마젠타)
        // 기준: 빨강과 파랑 150 이상, 빨강과 파랑 차이 40 이하, 초록 120 이하
        // (기획서 이미지 2번의 하단 '초록' 중복 기재 부분 반영)
        if (c.r >= magentaRule.minComposite && c.b >= magentaRule.minComposite &&
            Mathf.Abs(c.r - c.b) <= magentaRule.compositeMaxDifference && c.g <= magentaRule.maxOther)
            return JellyColorType.Magenta;

        // 6. Yellow (노랑)
        // 기준: 빨강과 초록 150 이상, 빨강과 초록 차이 40 이하, 파랑 120 이하
        if (c.r >= yellowRule.minComposite && c.g >= yellowRule.minComposite &&
            Mathf.Abs(c.r - c.g) <= yellowRule.compositeMaxDifference && c.b <= yellowRule.maxOther)
            return JellyColorType.Yellow;

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