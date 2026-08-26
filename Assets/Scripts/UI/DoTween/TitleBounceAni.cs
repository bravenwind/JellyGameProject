using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class TitleBounceAni : MonoBehaviour
{
    [Header("Target UI (RectTransform)")]
    public RectTransform target;

    [Header("Optional: Root Canvas (if null, auto)")]
    public Canvas rootCanvas;

    [Header("Shake Settings")]
    public float duration = 0.6f;
    public Vector3 strength = new Vector3(0.2f, 0.2f, 0f);
    public int vibrato = 14;
    [SerializeField] private float randomness = 90f;
    public bool fadeOut = true;
    [SerializeField] private ShakeRandomnessMode randomnessMode = ShakeRandomnessMode.Full;

    [Header("Options")]
    public bool playOnEnable = true;
    public bool ignoreTimeScale = true;

    [SerializeField] private bool playAfterLayoutReady = true;
    [SerializeField] private int delayFrames = 1;

    private Tween shakeTween;
    private Vector3 baseScale;
    private Coroutine playRoutine;

    private void Reset()
    {
        target = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
        if (rootCanvas == null && target != null)
            rootCanvas = target.GetComponentInParent<Canvas>();
    }

    private void OnEnable()
    {
        if (!playOnEnable)
            return;

        if (playAfterLayoutReady)
        {
            if (playRoutine != null)
                StopCoroutine(playRoutine);
            playRoutine = StartCoroutine(CoPlayAfterLayoutReady());
        }
        else
            PlayShakeImmediate();
    }

    private void OnDisable()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        Kill();

        if (target != null)
            target.localScale = baseScale;
    }

    private IEnumerator CoPlayAfterLayoutReady()
    {
        ForceUIRebuild();

        for (int i = 0; i < delayFrames; i++)
            yield return null;

        yield return new WaitForEndOfFrame();

        ForceUIRebuild();

        PlayShakeImmediate();
        playRoutine = null;
    }

    private void ForceUIRebuild()
    {
        if (target == null)
            return;

        Canvas.ForceUpdateCanvases();

        LayoutRebuilder.ForceRebuildLayoutImmediate(target);

    }

    public void PlayShakeImmediate()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
        if (target == null)
            return;

        Kill();

        baseScale = target.localScale;
        target.localScale = baseScale;

        shakeTween = target
            .DOShakeScale(duration, strength, vibrato, randomness, fadeOut, randomnessMode)
            .SetUpdate(ignoreTimeScale)
            .OnComplete(() =>
            {
                if (target != null)
                    target.localScale = baseScale;
            });
    }

    public void Kill()
    {
        if (shakeTween != null && shakeTween.IsActive())
            shakeTween.Kill();

        shakeTween = null;
    }
}
