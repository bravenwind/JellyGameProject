using System.Collections.Generic;
using UnityEngine;
using System;

public class DataManager : MonoBehaviour
{
    [Serializable]
    public class JellyEffectData
    {
        public JellyColorType type;
        public RYBColor rybChange;
    }

    public static DataManager Instance { get; private set; }

    [SerializeField] private float jellyScaleIncrease = 0.05f;

    [Range(0f, 1f)]
    [SerializeField] private float absorbScalePercent = 0.3f;

    [SerializeField] private float jumpScaleThreshold = 2f;

    [SerializeField] private float growAnimTime = 1.0f;

    [SerializeField] private float increaseJumpForceValue = 5;

    [SerializeField] private float batCooldown = 1.2f;

    [SerializeField] private float batSwingDuration = 0.35f;

    [SerializeField] private float batRange = 2.0f;

    [SerializeField] private float batPushForce = 18f;

    [SerializeField] private float batHitGrowth = 0.08f;

    [SerializeField] private float batArcAngle = 120f;

    [SerializeField] private float stepTileCollapseDelay = 2f;

    [SerializeField] private float stepTileWarningDuration = 1.5f;

    [SerializeField] private int stepTileStepsToCollapse = 3;

    [SerializeField] private float stepTileIdleWearSeconds = 2f;

    [SerializeField] private int stepTileDangerMargin = 1;

    [SerializeField] private int stepTileFootingMargin = 0;

    [SerializeField] private float cameraZoomDuration = 1.0f;

    [SerializeField] private float scaleChangedPlusSize = 3.0f;

    [SerializeField] private float cameraZoomFirstThreshold = 6f;

    [SerializeField] private float cameraZoomThresholdStep = 4f;

    [SerializeField] private int scorePerJelly = 100;

    [SerializeField] private List<JellyEffectData> jellyEffects;

    private Dictionary<JellyColorType, RYBColor> jellyEffectCache;

    public float JellyScaleIncrease { get { return jellyScaleIncrease; } }
    public float AbsorbScalePercent { get { return absorbScalePercent; } }
    public float JumpScaleThreshold { get { return jumpScaleThreshold; } }
    public float GrowAnimTime { get { return growAnimTime; } }
    public float IncreaseJumpForceValue { get { return increaseJumpForceValue; } }
    public float BatCooldown { get { return batCooldown; } }
    public float BatSwingDuration { get { return batSwingDuration; } }
    public float BatRange { get { return batRange; } }
    public float BatPushForce { get { return batPushForce; } }
    public float BatHitGrowth { get { return batHitGrowth; } }
    public float BatArcAngle { get { return batArcAngle; } }
    public float StepTileCollapseDelay { get { return stepTileCollapseDelay; } }
    public float StepTileWarningDuration { get { return stepTileWarningDuration; } }
    public int StepTileStepsToCollapse { get { return stepTileStepsToCollapse; } }
    public float StepTileIdleWearSeconds { get { return stepTileIdleWearSeconds; } }
    public int StepTileDangerMargin { get { return stepTileDangerMargin; } }
    public int StepTileFootingMargin { get { return stepTileFootingMargin; } }
    public float CameraZoomDuration { get { return cameraZoomDuration; } }
    public float ScaleChangedPlusSize { get { return scaleChangedPlusSize; } }
    public float CameraZoomFirstThreshold { get { return cameraZoomFirstThreshold; } }
    public float CameraZoomThresholdStep { get { return cameraZoomThresholdStep; } }
    public int ScorePerJelly { get { return scorePerJelly; } }

    public RYBColor GetJellyRYBEffect(JellyColorType type)
    {
        if (jellyEffectCache != null && jellyEffectCache.TryGetValue(type, out var cached))
            return cached;
        return new RYBColor(0, 0, 0);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        ValidateSettings();
        BuildJellyEffectCache();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void BuildJellyEffectCache()
    {
        jellyEffectCache = new Dictionary<JellyColorType, RYBColor>();

        if (jellyEffects == null)
            return;

        foreach (JellyEffectData data in jellyEffects)
        {
            if (data == null)
                continue;

            jellyEffectCache[data.type] = data.rybChange;
        }
    }

    private void ValidateSettings()
    {
        if (growAnimTime <= 0f)
            growAnimTime = 0.1f;

        if (scorePerJelly <= 0)
            scorePerJelly = 1;

        if (jellyScaleIncrease <= 0f)
            jellyScaleIncrease = 0.05f;

        if (cameraZoomThresholdStep <= 0f)
            cameraZoomThresholdStep = 4f;

        if (stepTileStepsToCollapse <= 0)
            stepTileStepsToCollapse = 3;

        if (stepTileFootingMargin >= stepTileStepsToCollapse)
            stepTileFootingMargin = Mathf.Max(1, stepTileStepsToCollapse - 1);
    }
}
