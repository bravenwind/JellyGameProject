using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

public enum JellyColorType
{
    Red, Yellow, Blue,          // RYB 기본색
    Orange, Green, Purple,      // RYB 혼합색
    White, Black, None          // 특수
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

    // ── RYB 젤리 효과 데이터 ──
    [System.Serializable]
    public class JellyEffectData
    {
        public string colorName;
        public JellyColorType type;
        public RYBColor rybChange;  // 이 젤리를 먹었을 때 추가되는 RYB 값

        [HideInInspector] public Vector3Int rgbChange; // (Legacy, 사용 안 함)
    }

    // 런타임 상태는 GameState 정적 클래스로 이동됨:
    // GameState.CurrentScore, GameState.PlayerCurrentScale,
    // GameState.CurrentRYBColor, GameState.CurrentDisplayColor

    [Header("JellySpawnSettings")]
    public float spawnCoolTime = 10.0f;

    [Header("Scale Settings")]
    public float jellyScaleIncrease = 0.05f;
    [Range(0f, 1f)]
    public float absorbScalePercent = 0.3f;
    public float minScale = 1f;
    public float maxScale = 5f;
    public float scaleDecreaseAmount = 0.3f;
    public float jumpScaleThreshold = 2f;
    // playerCurrentScale → GameState.PlayerCurrentScale로 이동
    public float scaleIncreaseTime = 1.0f;
    public float scaleDecreaseTime = 1.0f;

    public float IncreaseJumpForceValue = 5;

    [Header("Camera Settings")]
    public float scaleIncreaseDuration = 1.0f;
    public float scaleDecreaseDuration = 1.0f;
    public float scaleChangedPlusSize = 3.0f;
    public float cameraZoomFirstThreshold = 6f;
    public float cameraZoomThresholdStep = 4f;

    public float[] gameFailMinusSizePerLevel;

    [Header("Score Settings")]
    // currentScore → GameState.CurrentScore로 이동
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

    // currentDisplayColor → GameState.CurrentDisplayColor로 이동

    [Header("Jelly Effects (RYB)")]
    public List<JellyEffectData> jellyEffects;

    /// <summary>젤리 타입의 RYB 변화량 반환</summary>
    public RYBColor GetJellyRYBEffect(JellyColorType type)
    {
        var data = jellyEffects.Find(x => x.type == type);
        return data != null ? data.rybChange : new RYBColor(0, 0, 0);
    }

    /// <summary>현재 RYB 상태의 지배 색상 타입</summary>
    public JellyColorType GetCurrentColorType()
        => GameState.CurrentRYBColor.GetDominantType();

    /// <summary>현재 RYB를 RGB로 변환</summary>
    public Color GetCurrentDisplayColor()
        => GameState.CurrentRYBColor.ToRGB();

    /// <summary>특정 목표에 대한 현재 순도</summary>
    public float GetCurrentPurity(JellyColorType targetType)
        => GameState.CurrentRYBColor.GetPurity(targetType);

    /// <summary>RYB 색상 초기화 (하얀 젤리)</summary>
    public void ResetRYBColor()
    {
        GameState.CurrentRYBColor = RYBColor.white;
        GameState.CurrentDisplayColor = Color.white;
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
        GameState.Reset();
    }

}