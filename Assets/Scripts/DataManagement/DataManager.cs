using System.Collections.Generic;
using UnityEngine;
using System;

public enum JellyColorType
{
    Red, Yellow, Blue,
    Orange, Green, Purple,
    White, Black, None
}

public class DataManager : MonoBehaviour
{
    public static DataManager Instance;

    [Serializable]
    public class JellyEffectData
    {
        public JellyColorType type;
        public RYBColor rybChange;
    }

    [Header("Scale Settings")]
    [SerializeField] private float jellyScaleIncrease = 0.05f;
    public float JellyScaleIncrease { get { return jellyScaleIncrease; } }
    [Range(0f, 1f)]
    [SerializeField] private float absorbScalePercent = 0.3f;
    public float AbsorbScalePercent { get { return absorbScalePercent; } }
    [SerializeField] private float minScale = 1f;
    public float MinScale { get { return minScale; } }
    [SerializeField] private float maxScale = 5f;
    public float MaxScale { get { return maxScale; } }
    [SerializeField] private float jumpScaleThreshold = 2f;
    public float JumpScaleThreshold { get { return jumpScaleThreshold; } }
    [SerializeField] private float scaleIncreaseTime = 1.0f;
    public float ScaleIncreaseTime { get { return scaleIncreaseTime; } }
    [SerializeField] private float increaseJumpForceValue = 5;
    public float IncreaseJumpForceValue { get { return increaseJumpForceValue; } }

    [Header("Bat Attack Settings (Push Mode)")]
    [Tooltip("방망이 공격 쿨다운 (초)")]
    [SerializeField] private float batCooldown = 1.2f;
    public float BatCooldown { get { return batCooldown; } }
    [Tooltip("방망이 공격 지속 시간 (초)")]
    [SerializeField] private float batSwingDuration = 0.35f;
    public float BatSwingDuration { get { return batSwingDuration; } }
    [Tooltip("방망이 공격 범위 (플레이어 전방)")]
    [SerializeField] private float batRange = 2.0f;
    public float BatRange { get { return batRange; } }
    [Tooltip("방망이 밀치기 힘")]
    [SerializeField] private float batPushForce = 18f;
    public float BatPushForce { get { return batPushForce; } }
    [Tooltip("방망이 명중 시 성장량 (크기 1 기준)")]
    [SerializeField] private float batHitGrowth = 0.08f;
    public float BatHitGrowth { get { return batHitGrowth; } }
    [Tooltip("방망이 공격 전방 각도 (좌우 합산)")]
    [SerializeField] private float batArcAngle = 120f;
    public float BatArcAngle { get { return batArcAngle; } }

    [Header("Push Mode - Fall & Tile Settings")]
    [Tooltip("타일을 밟은 후 붕괴까지 딜레이 (초)")]
    [SerializeField] private float stepTileCollapseDelay = 2f;
    public float StepTileCollapseDelay { get { return stepTileCollapseDelay; } }
    [Tooltip("밟힌 타일 경고 흔들림 시간 (초)")]
    [SerializeField] private float stepTileWarningDuration = 1.5f;
    public float StepTileWarningDuration { get { return stepTileWarningDuration; } }
    [Tooltip("타일이 붕괴되기까지 필요한 밟은 횟수")]
    [SerializeField] private int stepTileStepsToCollapse = 3;
    public int StepTileStepsToCollapse { get { return stepTileStepsToCollapse; } }
    [Tooltip("한 타일 위에 가만히 머물 때 견디는 횟수가 1 감소하기까지의 시간 (초). 0 이하면 제자리 마모 비활성")]
    [SerializeField] private float stepTileIdleWearSeconds = 2f;
    public float StepTileIdleWearSeconds { get { return stepTileIdleWearSeconds; } }

    [Header("Camera Settings")]
    [SerializeField] private float scaleIncreaseDuration = 1.0f;
    public float ScaleIncreaseDuration { get { return scaleIncreaseDuration; } }
    [SerializeField] private float scaleDecreaseDuration = 1.0f;
    public float ScaleDecreaseDuration { get { return scaleDecreaseDuration; } }
    [SerializeField] private float scaleChangedPlusSize = 3.0f;
    public float ScaleChangedPlusSize { get { return scaleChangedPlusSize; } }
    [SerializeField] private float cameraZoomFirstThreshold = 6f;
    public float CameraZoomFirstThreshold { get { return cameraZoomFirstThreshold; } }
    [SerializeField] private float cameraZoomThresholdStep = 4f;
    public float CameraZoomThresholdStep { get { return cameraZoomThresholdStep; } }

    [Header("Score Settings")]
    [SerializeField] private int scorePerJelly = 100;
    [SerializeField] private float startingScale = 2f;
    public float StartingScale { get { return startingScale; } }

    public int ScoreFromScale(float scale)
    {
        if (jellyScaleIncrease <= 0f)
            return 0;
        return Mathf.Max(0, Mathf.RoundToInt((scale - startingScale) * scorePerJelly / jellyScaleIncrease));
    }

    [Header("Detection Settings")]

    [SerializeField] private LayerMask objectLayerMask ;
    public LayerMask ObjectLayerMask { get { return objectLayerMask; } }

    [Header("Jelly Effects (RYB)")]
    [SerializeField] private List<JellyEffectData> jellyEffects;

    private Dictionary<JellyColorType, RYBColor> jellyEffectCache;

    public RYBColor GetJellyRYBEffect(JellyColorType type)
    {
        if (jellyEffectCache != null && jellyEffectCache.TryGetValue(type, out var cached))
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

        // Reset()이 아니라 ResetValues()를 호출한다.
        // Reset()은 (1) 정적 이벤트 4종을 null로 말소해 — 이미 OnEnable에서 구독을 마친
        // 씬 HUD(CurrentStatusUI 등)의 구독이 끊겨 화면이 영구 동결될 수 있고,
        // (2) CurrentGameMode를 Absorb로 되돌려 Push 씬이 Absorb로 오독되는 레이스를 만든다.
        // Reset()은 도메인 리로드 대비용(SubsystemRegistration) 전용이다.
        GameState.ResetValues();

        objectLayerMask = LayerMask.GetMask("BackGroundObject");
    }

    private void BuildJellyEffectCache()
    {
        jellyEffectCache = new Dictionary<JellyColorType, RYBColor>();
        if (jellyEffects == null)
            return;
        foreach (var data in jellyEffects)
        {
            if (data != null)
                jellyEffectCache[data.type] = data.rybChange;
        }
    }

    private void ValidateSettings()
    {
        if (minScale > maxScale)
        {
            Debug.LogWarning($"[DataManager] minScale({minScale}) > maxScale({maxScale}), 값을 교정합니다.");
            (minScale, maxScale) = (maxScale, minScale);
        }

        if (scaleIncreaseTime <= 0f)
            scaleIncreaseTime = 0.1f;
        if (scorePerJelly <= 0)
            scorePerJelly = 1;
    }
}
