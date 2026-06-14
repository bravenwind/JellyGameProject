using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class LoadingCenterMultiAni : MonoBehaviour
{
    [System.Serializable]
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
    [Tooltip("Phase2 전체 길이(보통 로딩 대기시간으로 덮어씀). 이 시간 동안 가운데가 머문다.")]
    [SerializeField] private float phase2Duration = 2.0f;
    [Tooltip("실제로 흔드는 시간. phase2Duration보다 짧게 두면 처음에만 흔들리고 진정된 채 머문다.")]
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

    private void OnEnable()
    {
        if (!playOnEnable) return;
        RunAll();
    }

    private void OnDisable()
    {
        KillAll();
        RestoreAll();
    }

    public void RunAll()
    {
        PrepareTargets();
        KillAll();

        phase2Duration = GetComponentInParent<LoadingBGSlideAni>().holdSeconds;

        for (int i = 0; i < targets.Count; i++)
        {
            TargetUI t = targets[i];
            if (t.rect == null) continue;

            Sequence seq = DOTween.Sequence();
            if (ignoreTimeScale) seq.SetUpdate(true);

            if (t.delay > 0f)
                seq.AppendInterval(t.delay);

            // Init 상태: Phase1을 할 거면 startAlpha/scale로 세팅
            // Phase1을 안 하면 현재 상태를 유지하고, phase2/3가 켜져 있으면 그때만 조작
            if (t.doPhase1)
            {
                if (t.group != null) t.group.alpha = phase1StartAlpha;
                t.rect.localScale = t.baseScale * phase1StartScaleMul;

                // Phase 1
                if (t.group != null)
                    seq.Append(t.group.DOFade(phase1EndAlpha, phase1Duration).SetEase(phase1Ease));
                else
                    seq.AppendInterval(phase1Duration);

                seq.Join(t.rect.DOScale(t.baseScale * phase1EndScaleMul, phase1Duration).SetEase(phase1Ease));
            }

            // Phase 2 시간 구간은 전체 연출 길이 맞추기 위해 항상 확보
            // 단, doPhase2가 false면 그냥 가만히 유지
            if (t.doPhase2)
            {
                seq.AppendCallback(() =>
                {
                    Vector3 shakeBase = t.doPhase1 ? (t.baseScale * phase1EndScaleMul) : t.rect.localScale;
                    t.rect.localScale = shakeBase;

                    // 흔드는 시간은 phase2Duration이 아니라 phase2ShakeDuration. 처음에만 흔들고
                    // (shakeFadeOut으로 점점 약해지며) 진정된 뒤, 남은 시간은 아래 AppendInterval로 가만히 머문다.
                    float shakeDur = Mathf.Min(phase2ShakeDuration, phase2Duration);
                    Tween shake = t.rect.DOShakeScale(shakeDur, shakeStrength, shakeVibrato, shakeRandomness, shakeFadeOut, shakeRandomnessMode)
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

            seq.AppendInterval(phase2Duration);

            // Phase 3
            if (t.doPhase3)
            {
                if (t.group != null)
                    seq.Append(t.group.DOFade(phase3EndAlpha, phase3Duration).SetEase(phase3Ease));
                else
                    seq.AppendInterval(phase3Duration);

                seq.Join(t.rect.DOScale(t.baseScale * phase3EndScaleMul, phase3Duration).SetEase(phase3Ease));
            }

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
