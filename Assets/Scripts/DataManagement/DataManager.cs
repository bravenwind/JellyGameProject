using System.Collections.Generic;
using UnityEngine;
using System;

/// <summary>
/// 게임 규칙 수치를 인스펙터에서 조절하기 위한 통.
///
/// ★ 여기는 '숫자'만 둔다 — 규칙 계산은 두지 않는다
///   예전엔 ScoreFromScale(크기→점수 환산)이 여기 있었다. 그런데 그 계산은
///   기준 크기(NetEntity.BaselineScale)를 알아야 해서, <b>설정 클래스가 네트워크를
///   알아야 하는</b> 거꾸로 된 의존이 생겼다. 지금은 NetEntity.ScoreFromScale이 맡는다.
///
/// ★ 이 컴포넌트는 게임 씬에 반드시 하나 있어야 한다
///   없으면 Instance가 null이라 대부분의 접근이 NullReference로 터진다. 그게 맞다 —
///   규칙 수치가 없는 채로 조용히 굴러가는 것보다 즉시 멈추는 편이 찾기 쉽다.
///   (예전엔 곳곳에 서로 다른 폴백 리터럴이 있었고, 실제로 값이 어긋나 있었다)
/// </summary>
public class DataManager : MonoBehaviour
{
    // ★ 다른 싱글턴 11개와 같은 형태로 맞춘다
    //   예전엔 public 필드라 밖에서 통째로 덮어쓰거나 지울 수 있었다.
    public static DataManager Instance { get; private set; }

    [Serializable]
    public class JellyEffectData
    {
        public JellyColorType type;
        public RYBColor rybChange;
    }

    [Header("크기")]
    [Tooltip("젤리 하나를 먹을 때 늘어나는 크기")]
    [SerializeField] private float jellyScaleIncrease = 0.05f;
    public float JellyScaleIncrease { get { return jellyScaleIncrease; } }

    [Tooltip("남을 흡수했을 때, 상대 크기의 몇 %를 가져오는가")]
    [Range(0f, 1f)]
    [SerializeField] private float absorbScalePercent = 0.3f;
    public float AbsorbScalePercent { get { return absorbScalePercent; } }

    // 크기 상·하한은 없다 — 이 게임의 크기는 커지기만 하고, 씬의 상한도
    // 사실상 걸리지 않는 값(100)이었다. 클램프를 없애 '있는데 안 쓰는 값'을 지웠다.

    [Tooltip("이 크기를 넘으면 점프력이 올라간다")]
    [SerializeField] private float jumpScaleThreshold = 2f;
    public float JumpScaleThreshold { get { return jumpScaleThreshold; } }

    // ★ 예전 이름은 scaleIncreaseTime이었다
    //   아래 카메라의 cameraZoomDuration이 예전엔 scaleIncreaseDuration이라,
    //   인스펙터에서 둘을 구별할 수 없었다. 무엇이 커지는 시간인지 이름에 넣는다.
    [Tooltip("젤리가 커지는 연출에 걸리는 시간 (초)")]
    [SerializeField] private float growAnimTime = 1.0f;
    public float GrowAnimTime { get { return growAnimTime; } }

    [Tooltip("점프력 문턱을 넘었을 때 더해지는 점프력")]
    [SerializeField] private float increaseJumpForceValue = 5;
    public float IncreaseJumpForceValue { get { return increaseJumpForceValue; } }

    [Header("방망이 (밀치기 모드)")]
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

    [Header("발판 (밀치기 모드)")]
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

    // ═════════════════════════════════════════════════════════
    //  '위험한 칸'의 문턱이 둘인 이유
    // ═════════════════════════════════════════════════════════
    //
    //  같은 판정을 두 질문이 나눠 쓰고 있었다. 기준은 <b>그 칸에 얼마나 머무느냐</b>다.
    //    · 서 있는 칸: 계속 머물며 제자리 마모까지 먹는다 → 일찍 피한다
    //    · 지나갈 칸: 한순간 스칠 뿐이다 → 늦게까지 허용한다
    //  나눠 두면 둘을 따로 조일 수 있다. 지금은 같은 값이라 동작이 하나일 때와 같다.
    //
    // ★ 한때 footing 을 2로, stepsToCollapse 를 4로 올렸다가 되돌렸다
    //   "경보를 받고 붕괴까지 2.0초뿐인데 한 칸 벗어나는 데 2.33초라 늦는다"는
    //   계산 때문이었는데, <b>그 계산이 틀렸다.</b> stepTileCollapseDelay(2.0초)를
    //   빼먹었다. 실제 여유는 이렇다:
    //     count 2 경보 → 2.0초(제자리 마모) → count 3 붕괴 시작
    //                  → 2.0초(collapseDelay) → 바닥이 사라짐
    //     ⇒ 경보부터 바닥이 없어지기까지 4.0초. 걸어서 2.33초면 충분하다.
    //   문턱을 억지로 당길 이유가 없었고, footing 을 2로 두면 stepsToCollapse 3에서는
    //   count>=1 이 되어 <b>밟는 모든 칸이 도착 즉시 위험</b>이 된다(봇이 영원히 도망만 다닌다).
    [Tooltip("지나갈 칸 판정: 붕괴까지 이만큼 남으면 '위험'으로 본다. 경로·목적지 필터가 쓴다.")]
    [SerializeField] private int stepTileDangerMargin = 1;
    public int StepTileDangerMargin { get { return stepTileDangerMargin; } }

    [Tooltip("서 있는 칸 판정: 붕괴까지 이만큼 남으면 '발밑이 위험'으로 본다. 위보다 크거나 같아야 한다.")]
    [SerializeField] private int stepTileFootingMargin = 0;
    public int StepTileFootingMargin { get { return stepTileFootingMargin; } }

    [Header("카메라")]
    // ★ 예전 이름은 scaleIncreaseDuration이었다 (위 growAnimTime 주석 참고)
    [Tooltip("카메라가 한 칸 줌아웃하는 데 걸리는 시간 (초)")]
    [SerializeField] private float cameraZoomDuration = 1.0f;
    public float CameraZoomDuration { get { return cameraZoomDuration; } }

    [Tooltip("문턱을 넘을 때마다 늘어나는 카메라 크기")]
    [SerializeField] private float scaleChangedPlusSize = 3.0f;
    public float ScaleChangedPlusSize { get { return scaleChangedPlusSize; } }

    [Tooltip("첫 줌아웃이 일어나는 크기")]
    [SerializeField] private float cameraZoomFirstThreshold = 6f;
    public float CameraZoomFirstThreshold { get { return cameraZoomFirstThreshold; } }

    [Tooltip("그 뒤로 이만큼 커질 때마다 한 칸씩 더 줌아웃한다")]
    [SerializeField] private float cameraZoomThresholdStep = 4f;
    public float CameraZoomThresholdStep { get { return cameraZoomThresholdStep; } }

    [Header("점수")]
    [Tooltip("젤리 하나당 점수. 크기→점수 환산은 NetEntity.ScoreFromScale이 한다.")]
    [SerializeField] private int scorePerJelly = 100;
    public int ScorePerJelly { get { return scorePerJelly; } }

    [Header("젤리 색 효과 (RYB)")]
    [Tooltip("젤리 색마다 RYB에 더해지는 양. 기본 6색(빨/노/파/주/초/보)이 모두 있어야 한다.")]
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
        if (Instance != null && Instance != this)
        {
            //컴포넌트만 지운다. gameObject를 지우면 같은 오브젝트에 붙은
            //다른 컴포넌트까지 같이 죽어 사고가 커진다
            Destroy(this);
            return;
        }

        Instance = this;

        ValidateSettings();
        BuildJellyEffectCache();
    }

    private void OnDestroy()
    {
        //파괴된 객체를 가리키는 정적 참조를 남기지 않는다
        if (Instance == this)
            Instance = null;
    }

    // ═════════════════════════════════════════════════════════
    //  설정 검증 — 조용히 고장 나는 값을 잡는다
    // ═════════════════════════════════════════════════════════
    //
    // ★ 아래 값들이 0이면 에러 없이 게임이 이상해진다
    //   그때 원인을 역추적하는 데 몇 시간이 든다. 시작할 때 한 번 알려준다.

    private void BuildJellyEffectCache()
    {
        jellyEffectCache = new Dictionary<JellyColorType, RYBColor>();

        if (jellyEffects == null)
        {
            Debug.LogError("[설정] 젤리 색 효과 목록이 비어 있습니다 — 젤리를 먹어도 색이 변하지 않습니다.");
            return;
        }

        foreach (JellyEffectData data in jellyEffects)
        {
            if (data == null)
                continue;

            //같은 색이 두 번 들어가면 뒤엣것이 조용히 이긴다. 어느 쪽이 적용되는지
            //인스펙터만 봐서는 알 수 없으므로 알려준다
            if (jellyEffectCache.ContainsKey(data.type))
                Debug.LogWarning($"[설정] 젤리 색 효과에 {data.type}이(가) 두 번 있습니다 — 마지막 것만 쓰입니다.");

            jellyEffectCache[data.type] = data.rybChange;
        }

        WarnMissingJellyColors();
    }

    /// <summary>
    /// 기본 6색이 다 들어 있는지 본다.
    ///
    /// ★ 빠져 있으면 GetJellyRYBEffect가 (0,0,0)을 돌려준다
    ///   그 색 젤리를 먹으면 크기만 커지고 <b>색이 하나도 안 변한다.</b>
    ///   에러도 로그도 없어서 "이 색만 이상한데?"로 한참 헤매게 된다.
    /// </summary>
    private void WarnMissingJellyColors()
    {
        JellyColorType[] required =
        {
            JellyColorType.Red, JellyColorType.Yellow, JellyColorType.Blue,
            JellyColorType.Orange, JellyColorType.Green, JellyColorType.Purple
        };

        for (int i = 0; i < required.Length; i++)
        {
            if (!jellyEffectCache.ContainsKey(required[i]))
                Debug.LogWarning($"[설정] 젤리 색 효과에 {required[i]}이(가) 없습니다 — 그 색 젤리를 먹어도 색이 안 변합니다.");
        }
    }

    private void ValidateSettings()
    {
        if (growAnimTime <= 0f)
        {
            Debug.LogWarning("[설정] growAnimTime이 0 이하라 0.1로 올립니다.");
            growAnimTime = 0.1f;
        }

        if (scorePerJelly <= 0)
        {
            Debug.LogWarning("[설정] scorePerJelly가 0 이하라 1로 올립니다.");
            scorePerJelly = 1;
        }

        //0이면 젤리를 먹어도 안 커지고 점수도 안 오른다. 나눗셈의 분모이기도 하다
        if (jellyScaleIncrease <= 0f)
        {
            Debug.LogError("[설정] jellyScaleIncrease가 0 이하입니다 — 젤리를 먹어도 크기와 점수가 오르지 않습니다.");
            jellyScaleIncrease = 0.05f;
        }

        //GetScaleTier의 분모. 0이면 카메라 줌아웃이 영영 안 일어난다
        if (cameraZoomThresholdStep <= 0f)
        {
            Debug.LogError("[설정] cameraZoomThresholdStep이 0 이하입니다 — 카메라가 줌아웃하지 않습니다.");
            cameraZoomThresholdStep = 4f;
        }

        //0이면 타일을 밟는 순간 무너진다
        if (stepTileStepsToCollapse <= 0)
        {
            Debug.LogError("[설정] stepTileStepsToCollapse가 0 이하입니다 — 타일이 밟자마자 무너집니다.");
            stepTileStepsToCollapse = 3;
        }

        //발밑 문턱이 경로 문턱보다 이르지 않으면 나눈 의미가 없다
        if (stepTileFootingMargin < stepTileDangerMargin)
        {
            Debug.LogError($"[설정] stepTileFootingMargin({stepTileFootingMargin})이 "
                         + $"stepTileDangerMargin({stepTileDangerMargin})보다 작습니다 — 발밑을 경로보다 늦게 피합니다.");
            stepTileFootingMargin = stepTileDangerMargin;
        }

        //여유가 밟는 횟수 이상이면 새 타일도 처음부터 위험이 되어 갈 곳이 사라진다
        if (stepTileFootingMargin >= stepTileStepsToCollapse)
        {
            Debug.LogError($"[설정] stepTileFootingMargin({stepTileFootingMargin})이 "
                         + $"stepTileStepsToCollapse({stepTileStepsToCollapse}) 이상입니다 — 모든 칸이 위험이 됩니다.");
            stepTileFootingMargin = Mathf.Max(1, stepTileStepsToCollapse - 1);
        }

        //둘 중 하나라도 0이면 방망이가 아무도 못 맞힌다
        if (batRange <= 0f || batArcAngle <= 0f)
            Debug.LogError($"[설정] 방망이 사거리({batRange}) 또는 각도({batArcAngle})가 0 이하입니다 — 공격이 맞지 않습니다.");
    }
}
