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

    // [U1 정리] Dash/Push 설정(pushJellyThreshold·dashPushForce·PushScaleThreshold) 삭제 —
    // 유일 소비처였던 고아 대쉬 히트 RPC(NetworkPlayerSync)와 함께 제거 (2026-07-22).

    [Header("Bat Attack Settings (Push Mode)")]
    [Tooltip("방망이 공격 쿨다운 (초)")]
    public float batCooldown = 1.2f;
    [Tooltip("방망이 공격 지속 시간 (초)")]
    public float batSwingDuration = 0.35f;
    [Tooltip("방망이 공격 범위 (플레이어 전방)")]
    public float batRange = 2.0f;
    [Tooltip("방망이 밀치기 힘")]
    public float batPushForce = 18f;
    [Tooltip("방망이 명중 시 성장량 (크기 1 기준)")]
    public float batHitGrowth = 0.08f;
    [Tooltip("방망이 공격 전방 각도 (좌우 합산)")]
    public float batArcAngle = 120f;

    [Header("Push Mode - Fall & Tile Settings")]
    [Tooltip("이 Y좌표 아래로 떨어지면 낙사 판정")]
    public float fallOffThreshold = -10f;
    [Tooltip("타일을 밟은 후 붕괴까지 딜레이 (초)")]
    public float stepTileCollapseDelay = 2f;
    [Tooltip("밟힌 타일 경고 흔들림 시간 (초)")]
    public float stepTileWarningDuration = 1.5f;
    [Tooltip("타일이 붕괴되기까지 필요한 밟은 횟수")]
    public int stepTileStepsToCollapse = 3;
    [Tooltip("한 타일 위에 가만히 머물 때 견디는 횟수가 1 감소하기까지의 시간 (초). 0 이하면 제자리 마모 비활성")]
    public float stepTileIdleWearSeconds = 2f;

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

        // [W1/V3] Reset()이 아니라 ResetValues()를 호출한다.
        // Reset()은 (1) 정적 이벤트 4종을 null로 말소해 — 이미 OnEnable에서 구독을 마친
        // 씬 HUD(CurrentStatusUI 등)의 구독이 끊겨 화면이 영구 동결될 수 있고(W1),
        // (2) CurrentGameMode를 Absorb로 되돌려 룸 권위 복원(GameModeManager)과의
        // Start 순서에 따라 Push 씬이 Absorb로 오독되는 레이스를 만든다(V3).
        // Reset()은 도메인 리로드 대비용(SubsystemRegistration) 전용이다. (H1 원칙)
        GameState.ResetValues();
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
