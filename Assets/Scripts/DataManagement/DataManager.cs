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

    [Header("Detection Settings")]
    public float originalDetectRadius = 4;
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

    [Header("Jelly Effects (RYB)")]
    public List<JellyEffectData> jellyEffects;

    public RYBColor GetJellyRYBEffect(JellyColorType type)
    {
        var data = jellyEffects.Find(x => x.type == type);
        return data != null ? data.rybChange : new RYBColor(0, 0, 0);
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

        GameState.Reset();
        GameState.DetectRadius = originalDetectRadius;
    }
}
