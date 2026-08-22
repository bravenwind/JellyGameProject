using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using System;

public class LoadingCenterMultiAni : MonoBehaviour
{
    [Serializable]
    public class TargetUI
    {
        public RectTransform rect;
        public CanvasGroup group;     // 비워도 됨(자동 생성)
        public float delay;

        [Header("Per-Target Toggles")]
        public bool doPhase1 = true;  // Fade In + Shrink
        public bool doPhase2 = true;  // Jelly (DOShakeScale)
        public bool doPhase3 = true;  // Grow + Fade Out

        [HideInInspector] public Vector3 baseScale;
    }

    [Header("Targets")]
    [SerializeField] private List<TargetUI> targets = new List<TargetUI>();

    [Header("Phase 1: Fade In + Shrink")]
    [SerializeField] private float phase1Duration = 0.5f;
    [SerializeField] private float phase1StartAlpha = 0f;
    [SerializeField] private float phase1EndAlpha = 1f;
    [SerializeField] private float phase1StartScaleMul = 1.15f;
    [SerializeField] private float phase1EndScaleMul = 0.90f;
    [SerializeField] private Ease phase1Ease = Ease.OutCubic;

    [Header("Phase 2: Jelly (DOShakeScale)")]
    [Tooltip("실제로 흔드는 시간. 로딩 대기시간보다 짧게 두면 처음에만 흔들리고 진정된 채 머문다.")]
    [SerializeField] private float phase2ShakeDuration = 0.6f;
    [SerializeField] private Vector3 shakeStrength = new Vector3(0.12f, 0.12f, 0f);
    [SerializeField] private int shakeVibrato = 18;
    [SerializeField] private float shakeRandomness = 30f;
    [SerializeField] private bool shakeFadeOut = true;
    [SerializeField] private ShakeRandomnessMode shakeRandomnessMode = ShakeRandomnessMode.Harmonic;

    [Header("Phase 3: Grow + Fade Out")]
    [SerializeField] private float phase3Duration = 0.5f;
    [SerializeField] private float phase3EndAlpha = 0f;
    [SerializeField] private float phase3EndScaleMul = 1.20f;
    [SerializeField] private Ease phase3Ease = Ease.InCubic;

    [Header("Options")]
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool ignoreTimeScale = true;

    private readonly List<Sequence> runningSeqs = new List<Sequence>();

    // [동기화] 부모 BG 슬라이드. Phase3(사라짐)를 BG의 센터->오른쪽(퇴장) 시작에 맞춰 재생하기 위해 구독한다.
    private LoadingBGSlideAni _bgSlide;
    private bool _exitPlayed;

    private void OnEnable()
    {
        // BG 슬라이드의 '퇴장 시작'에 Phase3를 맞추기 위해 구독(재생 여부와 무관하게 먼저 건다).
        _bgSlide = GetComponentInParent<LoadingBGSlideAni>();
        if (_bgSlide != null) _bgSlide.ExitStarted += OnBGExitStarted;

        if (!playOnEnable) return;
        _exitPlayed = false;
        RunAll();
    }

    private void OnDisable()
    {
        if (_bgSlide != null)
        {
            _bgSlide.ExitStarted -= OnBGExitStarted;
            _bgSlide = null;
        }
        KillAll();
        RestoreAll();
    }

    // 등장(Phase1) + 젤리 흔들림(Phase2)까지만 재생하고, 그 뒤엔 '센터에서 가만히 유지'한다.
    // Phase3(사라짐)는 여기서 붙이지 않는다 → BG 슬라이드아웃이 시작될 때 OnBGExitStarted→PlayPhase3로 재생해
    // BG 커튼(센터->오)과 '동시에' 사라지게 한다. (예전엔 phase2Duration=holdSeconds 고정이라 BG보다 먼저 사라졌다.)
    public void RunAll()
    {
        PrepareTargets();
        KillAll();

        for (int i = 0; i < targets.Count; i++)
        {
            TargetUI t = targets[i];
            if (t.rect == null) continue;

            Sequence seq = DOTween.Sequence();
            if (ignoreTimeScale) seq.SetUpdate(true);

            if (t.delay > 0f)
                seq.AppendInterval(t.delay);

            // Phase 1: Fade In + Shrink (등장 — BG의 왼->센터 동안)
            if (t.doPhase1)
            {
                if (t.group != null) t.group.alpha = phase1StartAlpha;
                t.rect.localScale = t.baseScale * phase1StartScaleMul;

                if (t.group != null)
                    seq.Append(t.group.DOFade(phase1EndAlpha, phase1Duration).SetEase(phase1Ease));
                else
                    seq.AppendInterval(phase1Duration);

                seq.Join(t.rect.DOScale(t.baseScale * phase1EndScaleMul, phase1Duration).SetEase(phase1Ease));
            }

            // Phase 2: 처음에만 흔들고(phase2ShakeDuration) 진정된 채 '가만히 유지'. 고정 대기 시간을 두지 않는다.
            if (t.doPhase2)
            {
                seq.AppendCallback(() =>
                {
                    Vector3 shakeBase = t.doPhase1 ? (t.baseScale * phase1EndScaleMul) : t.rect.localScale;
                    t.rect.localScale = shakeBase;

                    Tween shake = t.rect.DOShakeScale(phase2ShakeDuration, shakeStrength, shakeVibrato, shakeRandomness, shakeFadeOut, shakeRandomnessMode)
                        .SetUpdate(ignoreTimeScale)
                        .OnComplete(() =>
                        {
                            if (t.rect != null) t.rect.localScale = shakeBase;
                        });

                    seq.OnKill(() =>
                    {
                        if (shake != null && shake.IsActive()) shake.Kill();
                    });
                });
            }

            // (Phase3 없음 — 여기서 시퀀스가 끝나면 타깃은 등장 완료 상태로 센터에서 유지된다.)
            runningSeqs.Add(seq);
        }
    }

    // BG 커튼이 센터->오른쪽으로 나가기 시작할 때 호출 → 센터 이미지도 Phase3(커지며 사라짐)로 동시에 퇴장.
    private void OnBGExitStarted()
    {
        if (_exitPlayed) return;
        _exitPlayed = true;
        PlayPhase3();
    }

    private void PlayPhase3()
    {
        KillAll(); // 유지 중이던 등장/흔들림 시퀀스 정지

        for (int i = 0; i < targets.Count; i++)
        {
            TargetUI t = targets[i];
            if (t.rect == null || !t.doPhase3) continue;

            Sequence seq = DOTween.Sequence();
            if (ignoreTimeScale) seq.SetUpdate(true);

            if (t.group != null)
                seq.Append(t.group.DOFade(phase3EndAlpha, phase3Duration).SetEase(phase3Ease));
            else
                seq.AppendInterval(phase3Duration);

            seq.Join(t.rect.DOScale(t.baseScale * phase3EndScaleMul, phase3Duration).SetEase(phase3Ease));

            runningSeqs.Add(seq);
        }
    }

    private void PrepareTargets()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            TargetUI t = targets[i];
            if (t.rect == null) continue;

            t.baseScale = t.rect.localScale;

            if (t.group == null)
                t.group = t.rect.GetComponent<CanvasGroup>();

            if (t.group == null)
                t.group = t.rect.gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void KillAll()
    {
        for (int i = 0; i < runningSeqs.Count; i++)
        {
            Sequence s = runningSeqs[i];
            if (s != null && s.IsActive()) s.Kill();
        }
        runningSeqs.Clear();
    }

    private void RestoreAll()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            TargetUI t = targets[i];
            if (t.rect == null) continue;

            t.rect.localScale = t.baseScale;
            if (t.group != null) t.group.alpha = 1f;
        }
    }
}
