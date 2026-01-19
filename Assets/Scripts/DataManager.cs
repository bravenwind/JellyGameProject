using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public enum JellyColorType
{
    Red, Green, Blue,
    Yellow, Cyan, Magenta, White, Black, Temp
}

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    [Header("Color Settings")]
    public ColorSet[] playerColors;
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
        if (playerColors.Length > 0)
        {
            initialColorSet = playerColors[0];
            currentColorSet = initialColorSet; // 현재 상태 초기화 필수
            targetColorSet = initialColorSet;
        }
    }
}
