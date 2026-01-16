using Unity.Collections;
using UnityEngine;

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    [Header("Color Settings")]
    public ColorSet initialColorSet;
    public ColorSet currentColorSet;
    public ColorSet targetColorSet;
    public Color currentColor;
    public Color targetColor;
    public int changeColorJellyCount;

    public int eatenRedJellyCount = 0;
    public int eatenGreenJellyCount = 0;
    public int eatenBlueJellyCount = 0;

    // 데이터 구조 예시
    [System.Serializable]
    public struct ColorSet
    {
        public string colorName;
        public Material colorMaterial;
        public Color weak;  // 1단계
        public Color normal;   // 2단계
        public Color strong;   // 3단계
    }

    public ColorSet[] playerColors;

    // 빨간색 세트 정의
    ColorSet redPalette = new ColorSet
    {
        weak = new Color(1.0f, 0.6f, 0.6f), // 연한 빨강
        normal = Color.red,                    // 기본 빨강
        strong = new Color(0.6f, 0f, 0f)       // 진한 빨강
    };


    [Header("Color Increment Settings (Vector3: X=R, Y=G, Z=B)")]
    public Vector3 redPlusColor = new Vector3(30, 0, 0);
    public Vector3 greenPlusColor = new Vector3(0, 30, 0);
    public Vector3 bluePlusColor = new Vector3(0, 0, 30);

    // 혼합색 (Yellow = R+G, Magenta = R+B, Cyan = G+B)
    public Vector3 yellowPlusColor = new Vector3(20, 20, 0);
    public Vector3 magentaPlusColor = new Vector3(20, 0, 20);
    public Vector3 cyanPlusColor = new Vector3(0, 20, 20);

    // 빼는 색 (음수값 사용)
    public Vector3 whitePlusColor = new Vector3(-30, -30, -30);

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
        Instance = this;
        playerCurrentLevel = 1;
        absorbedJellyCount = 0;
        addScalePerLevel = new float[maxLevel];
        currentScore = 0;
        for (int i = 0; i < addScalePerLevel.Length; i++)
        {
            addScalePerLevel[i] = 1.5f;
        }
        initialColorSet = playerColors[0];
        currentColorSet = initialColorSet;
    }
}
