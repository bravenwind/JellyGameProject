using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public enum JellyColorType
{
    Red, Green, Blue,
    Yellow, Cyan, Magenta, White, Black, Temp
}

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    [Header("Color Settings (Enemy)")]
    public ColorSet[] enemyJellyColorSets; // 기존 에너미용

    [Header("Color Settings (Player)")]
    public ColorSet[] playerJellyColorSets; // [추가] 플레이어용

    [Header("Current Status")]
    public ColorSet initialColorSet;
    public ColorSet currentColorSet;
    public ColorSet targetColorSet;
    public int currentColorIntensity = 2;
    public int targetColorIntensity = 2;
    public Color currentColor;
    public Color targetColor;
    public int currentColorJellyCount;
    public int changeColorJellyCount;

    public List<JellyColorType> jellyBuffer = new List<JellyColorType>();

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

    [Header("Level Settings")]
    public int levelUpExp = 5;
    public float[] addScalePerLevel;
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

    [Header("Data Access")]
    public JellyDataDAO jellyDAO;
    public List<JellyDataDTO> loadedEnemyData;  // 확인용
    public List<JellyDataDTO> loadedPlayerData; // 확인용 [추가]

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
        LoadAndApplyData(); // 데이터 로드 및 적용
    }

    private void InitializeGameData()
    {
        playerCurrentLevel = 1;
        absorbedJellyCount = 0;
        addScalePerLevel = new float[maxLevel];
        currentScore = 0;

        for (int i = 0; i < addScalePerLevel.Length; i++)
        {
            addScalePerLevel[i] = 1.5f;
        }
    }

    private void LoadAndApplyData()
    {
        // 1. DAO 초기화
        if (jellyDAO == null) jellyDAO = gameObject.AddComponent<JellyDataDAO>();

        // 2. CSV 데이터 로드 (DAO 호출)
        loadedEnemyData = jellyDAO.LoadJellyData();   // 기존 (에너미)
        loadedPlayerData = jellyDAO.LoadPlayerData(); // 신규 (플레이어)

        // 3. 데이터를 ColorSet으로 변환하여 적용
        // 에너미 젤리 적용
        if (loadedEnemyData != null)
        {
            enemyJellyColorSets = ConvertDtosToColorSets(loadedEnemyData);
        }

        // 플레이어 젤리 적용 [추가]
        if (loadedPlayerData != null)
        {
            playerJellyColorSets = ConvertDtosToColorSets(loadedPlayerData);
        }
        else
        {
            Debug.LogWarning("플레이어 데이터가 로드되지 않았습니다. (Resources/Data/PlayerData.csv 확인 필요)");
        }

        // 4. 초기값 설정 (에너미 데이터 기준 예시, 필요시 플레이어로 변경 가능)
        if (playerJellyColorSets != null && playerJellyColorSets.Length > 0)
        {
            initialColorSet = playerJellyColorSets[0];
            currentColorSet = initialColorSet;
            targetColorSet = initialColorSet;
        }

        Debug.Log($"데이터 로드 완료 - Enemy: {playerJellyColorSets?.Length ?? 0}개, Player: {playerJellyColorSets?.Length ?? 0}개");
    }

    // [핵심] DTO 리스트를 ColorSet 배열로 변환하는 공통 함수
    private ColorSet[] ConvertDtosToColorSets(List<JellyDataDTO> dataList)
    {
        if (dataList == null || dataList.Count == 0) return new ColorSet[0];

        // 1. 그룹화
        Dictionary<JellyColorType, List<JellyDataDTO>> groupedData = new Dictionary<JellyColorType, List<JellyDataDTO>>();
        foreach (var dto in dataList)
        {
            if (!groupedData.ContainsKey(dto.ColorType))
            {
                groupedData[dto.ColorType] = new List<JellyDataDTO>();
            }
            groupedData[dto.ColorType].Add(dto);
        }

        // 2. ColorSet 생성
        List<ColorSet> resultColorSets = new List<ColorSet>();

        foreach (var group in groupedData)
        {
            JellyColorType type = group.Key;
            List<JellyDataDTO> dtos = group.Value;

            ColorSet set = new ColorSet();
            set.colorType = type;
            set.colorName = type.ToString();

            // 강도별 색상 할당
            JellyDataDTO weakDto = dtos.Find(d => d.ColorIntensity == 1);
            if (weakDto != null) set.weak = weakDto.GetColor();

            JellyDataDTO normalDto = dtos.Find(d => d.ColorIntensity == 2);
            if (normalDto != null) set.normal = normalDto.GetColor();

            JellyDataDTO strongDto = dtos.Find(d => d.ColorIntensity == 3);
            if (strongDto != null) set.strong = strongDto.GetColor();

            // 머티리얼 로드
            JellyDataDTO representativeDto = normalDto ?? weakDto ?? strongDto;
            if (representativeDto != null)
            {
                // 경로 예시: Models/BearJelly/Materials/MaterialName
                // 주의: 플레이어와 에너미의 머티리얼 폴더 경로가 다르다면 DTO에 경로 필드를 추가하거나 여기서 분기 처리가 필요할 수 있음.
                // 현재는 CSV의 'MaterialPath' 값을 그대로 사용한다고 가정.
                string path = $"Models/BearJelly/Materials/{representativeDto.MaterialPath}";
                set.colorMaterial = Resources.Load<Material>(path);

                if (set.colorMaterial == null)
                {
                    // 혹시 경로가 다를 수 있으니 로그만 띄움
                    // Debug.LogWarning($"머티리얼 로드 실패: {path}");
                }
            }

            resultColorSets.Add(set);
        }

        return resultColorSets.ToArray();
    }
}