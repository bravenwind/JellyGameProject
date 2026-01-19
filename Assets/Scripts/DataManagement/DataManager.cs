using System.Collections.Generic;
using UnityEngine;
using System.Linq; // 리스트 필터링을 위해 추가 (FindAll 등)

public enum JellyColorType
{
    Red, Green, Blue,
    Yellow, Cyan, Magenta, White, Black, Temp
}

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    [Header("Color Settings")]
    public ColorSet[] jellyColorSets;
    public ColorSet initialColorSet;
    public ColorSet currentColorSet;
    public ColorSet targetColorSet;
    public int currentColorIntensity = 2;
    public int targetColorIntensity = 2;
    public Color currentColor;
    public Color targetColor;
    public int currentColorJellyCount;
    public int changeColorJellyCount;

    // 색상 변경을 위한 버퍼 (3개를 모았을 때 판정)
    public List<JellyColorType> jellyBuffer = new List<JellyColorType>();

    // 데이터 구조 예시
    [System.Serializable]
    public struct ColorSet
    {
        public string colorName;
        public Material colorMaterial;
        public JellyColorType colorType;
        public Color weak;  // 1단계
        public Color normal;   // 2단계
        public Color strong;   // 3단계
    }

    //[Header("Color Increment Settings (Vector3: X=R, Y=G, Z=B)")]
    //public Vector3 redPlusColor = new Vector3(30, 0, 0);
    //public Vector3 greenPlusColor = new Vector3(0, 30, 0);
    //public Vector3 bluePlusColor = new Vector3(0, 0, 30);

    //// 혼합색 (Yellow = R+G, Magenta = R+B, Cyan = G+B)
    //public Vector3 yellowPlusColor = new Vector3(20, 20, 0);
    //public Vector3 magentaPlusColor = new Vector3(20, 0, 20);
    //public Vector3 cyanPlusColor = new Vector3(0, 20, 20);

    //// 빼는 색 (음수값 사용)
    //public Vector3 whitePlusColor = new Vector3(-30, -30, -30);

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
    public JellyDataDAO jellyDAO; // 인스펙터에서 할당하거나 코드로 추가
    public List<JellyDataDTO> loadedJellyData; // 로드된 데이터 확인용

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // 필요 시 주석 해제
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        playerCurrentLevel = 1;
        absorbedJellyCount = 0;
        addScalePerLevel = new float[maxLevel];
        currentScore = 0;

        for (int i = 0; i < addScalePerLevel.Length; i++)
        {
            addScalePerLevel[i] = 1.5f;
        }

        // [수정됨] 초기 색상 세팅 확실하게 초기화
        if (jellyColorSets.Length > 0)
        {
            initialColorSet = jellyColorSets[0];
            currentColorSet = initialColorSet; // 현재 상태 초기화 필수
            targetColorSet = initialColorSet;
        }

        // DAO 설정 및 데이터 로드
        if (jellyDAO == null) jellyDAO = gameObject.AddComponent<JellyDataDAO>();
        loadedJellyData = jellyDAO.LoadJellyData();

        // ★ CSV 데이터로 ColorSet 채우기 (여기서 호출) ★
        ApplyCsvDataToGame();

        // 데이터 로드 후 초기 세팅 (jellyColorSets가 채워진 후 실행해야 안전)
        if (jellyColorSets != null && jellyColorSets.Length > 0)
        {
            initialColorSet = jellyColorSets[0];
            currentColorSet = initialColorSet;
            targetColorSet = initialColorSet;
        }
    }

    private void ApplyCsvDataToGame()
    {
        if (loadedJellyData == null || loadedJellyData.Count == 0)
        {
            Debug.LogWarning("로딩된 젤리 데이터가 없습니다.");
            return;
        }

        // 1. 데이터를 ColorType 별로 그룹화하기 위한 딕셔너리 생성
        // Key: JellyColorType (Red, Blue...), Value: 해당 색상의 모든 DTO 리스트 (Weak, Normal, Strong)
        Dictionary<JellyColorType, List<JellyDataDTO>> groupedData = new Dictionary<JellyColorType, List<JellyDataDTO>>();

        foreach (var dto in loadedJellyData)
        {
            if (!groupedData.ContainsKey(dto.ColorType))
            {
                groupedData[dto.ColorType] = new List<JellyDataDTO>();
            }
            groupedData[dto.ColorType].Add(dto);
        }

        // 2. 그룹화된 데이터를 바탕으로 ColorSet 리스트 생성
        List<ColorSet> newColorSets = new List<ColorSet>();

        foreach (var group in groupedData)
        {
            JellyColorType type = group.Key;       // 현재 처리 중인 색상 타입 (예: Red)
            List<JellyDataDTO> dtos = group.Value; // 해당 색상의 데이터들 (강도 1,2,3)

            ColorSet set = new ColorSet();
            set.colorType = type;
            set.colorName = type.ToString();

            // 3. 각 강도(Intensity)에 맞는 데이터 찾아서 색상 할당
            // DTO에 GetColor() 함수가 있다고 가정 (이전 답변 코드 참고)
            // 만약 GetColor()가 없다면: new Color(dto.R/255f, dto.G/255f, dto.B/255f) 로 직접 변환

            JellyDataDTO weakDto = dtos.Find(d => d.ColorIntensity == 1);
            if (weakDto != null) set.weak = weakDto.GetColor();

            JellyDataDTO normalDto = dtos.Find(d => d.ColorIntensity == 2);
            if (normalDto != null) set.normal = normalDto.GetColor();

            JellyDataDTO strongDto = dtos.Find(d => d.ColorIntensity == 3);
            if (strongDto != null) set.strong = strongDto.GetColor();

            // 4. 머티리얼 로드 (Normal 등급의 머티리얼을 기준으로 하거나, 공통 사용)
            // Resources 폴더 경로 주의: "Materials/" + 파일이름
            JellyDataDTO representativeDto = normalDto ?? weakDto ?? strongDto;
            if (representativeDto != null)
            {
                // CSV에 "BearJelly_Red"라고 적혀있다면 -> Resources/Materials/BearJelly_Red 로드 시도
                // 경로는 실제 프로젝트 구조에 맞게 수정 필요
                string path = $"Models/BearJelly/Materials/{representativeDto.MaterialPath}";
                set.colorMaterial = Resources.Load<Material>(path);

                if (set.colorMaterial == null)
                {
                    Debug.LogWarning($"머티리얼을 찾을 수 없습니다: {path}");
                }
            }

            newColorSets.Add(set);
        }

        // 5. 리스트를 배열로 변환하여 DataManager 변수에 할당
        jellyColorSets = newColorSets.ToArray();

        Debug.Log($"CSV 데이터를 통해 {jellyColorSets.Length}개의 ColorSet을 생성했습니다.");
    }
}
