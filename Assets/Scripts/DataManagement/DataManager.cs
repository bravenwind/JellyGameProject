using System.Collections.Generic;
using UnityEngine;

public enum JellyColorType
{
    Red, Yellow, Blue,
    Orange, Green, Purple,
    White, Black, None
}

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    [System.Serializable]
    public struct ColorSet
    {
        public string colorName;
        public Material colorMaterial;
        public JellyColorType colorType;
        public Color weak;
        public Color normal;
        public Color strong;
    }

    [System.Serializable]
    public class JellyEffectData
    {
        public string colorName;
        public JellyColorType type;
        public RYBColor rybChange;
    }

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
    public int targetScore = 1000;
    public int scorePerJelly = 100;
    public float startingScale = 2f;

    public int ScoreFromScale(float scale)
    {
        if (jellyScaleIncrease <= 0f) return 0;
        return Mathf.Max(0, Mathf.RoundToInt((scale - startingScale) * scorePerJelly / jellyScaleIncrease));
    }

    [Header("Detection Settings")]
    public float originalDetectRadius = 4;
    public float detectPlusRadiusPerLevel = 1.5f;
    public LayerMask detectLayerMask;

    public LayerMask objectLayerMask;

    [System.Serializable]
    public struct MissionSet
    {
        public string missionName;
        public bool missionCleared;
    }

    [Header("Mission Settings")]
    public MissionSet[] missions;
    public float targetTime = 60;

    [Header("Jelly Effects (RYB)")]
    public List<JellyEffectData> jellyEffects;

    private Dictionary<JellyColorType, RYBColor> _jellyEffectCache;

    public RYBColor GetJellyRYBEffect(JellyColorType type)
    {
        if (_jellyEffectCache != null && _jellyEffectCache.TryGetValue(type, out var cached))
            return cached;
        return new RYBColor(0, 0, 0);
    }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        ValidateSettings();
        BuildJellyEffectCache();

        GameState.Reset();
        GameState.DetectRadius = originalDetectRadius;

        objectLayerMask = LayerMask.GetMask("BackGroundObject");
    }

    private void BuildJellyEffectCache()
    {
        _jellyEffectCache = new Dictionary<JellyColorType, RYBColor>();
        if (jellyEffects == null) return;
        foreach (var data in jellyEffects)
        {
            if (data != null)
                _jellyEffectCache[data.type] = data.rybChange;
        }
    }

    private void ValidateSettings()
    {
        if (minScale > maxScale)
        {
            Debug.LogWarning($"[DataManager] minScale({minScale}) > maxScale({maxScale}), 값을 교정합니다.");
            (minScale, maxScale) = (maxScale, minScale);
        }

        if (scaleIncreaseTime <= 0f) scaleIncreaseTime = 0.1f;
        if (scaleDecreaseTime <= 0f) scaleDecreaseTime = 0.1f;
        if (scorePerJelly <= 0) scorePerJelly = 1;
    }
}
