using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UIFollowTarget : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform targetObj;
    [Tooltip("타겟 위치로부터의 오프셋")]
    public Vector3 worldOffset = new Vector3(0, 2.0f, 0);

    [Header("Animation Settings")]
    [Tooltip("전체 애니메이션 지속 시간")]
    public float duration = 1.0f;

    [Tooltip("애니메이션 도달 시 최대 크기 배율")]
    public float maxScale = 0.75f;

    [Tooltip("크기 변화 곡선 (0~1 사이 값)")]
    public AnimationCurve scaleCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 1.0f),
        new Keyframe(1f, 0f)
    );

    [Tooltip("투명도 변화 곡선 (0~1 사이 값)")]
    public AnimationCurve alphaCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.1f, 1f),
        new Keyframe(0.8f, 1f),
        new Keyframe(1f, 0f)
    );

    [Header("Sprite Sequence (optional)")]
    [Tooltip("설정 시 스프라이트를 순차 재생합니다")]
    public Sprite[] spriteSequence;
    [Tooltip("각 스프라이트의 재생 시간")]
    public float durationPerImage = 0.5f;

    private Camera mainCamera;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Image targetImage;
    private Vector3 lastTargetPos;
    private bool useSpriteSequence;

    void Awake()
    {
        mainCamera = Camera.main;
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        targetImage = GetComponent<Image>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        useSpriteSequence = spriteSequence != null && spriteSequence.Length > 0;
    }

    private void OnEnable()
    {
        if (useSpriteSequence) return;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;

        SetTarget(playerObj.transform);
    }

    private void OnDisable()
    {
        ClearTarget();
    }

    public void SetTarget(Transform newTarget)
    {
        targetObj = newTarget;

        if (targetObj != null)
        {
            lastTargetPos = targetObj.position;
            UpdatePosition();
        }

        if (useSpriteSequence)
            StartCoroutine(PlaySpriteSequence());
        else
            StartCoroutine(PlayEffectProcess());
    }

    public void ClearTarget()
    {
        StopAllCoroutines();
        targetObj = null;
        canvasGroup.alpha = 1f;
        transform.localScale = Vector3.one;
    }

    IEnumerator PlayEffectProcess()
    {
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = timer / duration;

            float scaleValue = scaleCurve.Evaluate(progress) * maxScale;
            transform.localScale = new Vector3(scaleValue, scaleValue, 1f);

            float alphaValue = alphaCurve.Evaluate(progress);
            canvasGroup.alpha = alphaValue;

            yield return null;
        }

        UIPoolManager.Instance?.ReturnUI(gameObject);
    }

    IEnumerator PlaySpriteSequence()
    {
        for (int i = 0; i < spriteSequence.Length; i++)
        {
            if (targetImage != null) targetImage.sprite = spriteSequence[i];

            float timer = 0f;
            while (timer < durationPerImage)
            {
                timer += Time.deltaTime;
                float progress = timer / durationPerImage;

                float scaleValue = scaleCurve.Evaluate(progress);
                transform.localScale = new Vector3(scaleValue, scaleValue, 1f);

                float alphaValue = alphaCurve.Evaluate(progress);
                canvasGroup.alpha = alphaValue;

                yield return null;
            }
        }

        UIPoolManager.Instance?.ReturnUI(gameObject);
    }

    void LateUpdate()
    {
        if (targetObj != null)
        {
            lastTargetPos = targetObj.position;
        }

        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (mainCamera == null) return;

        Vector3 targetWorldPos = lastTargetPos + worldOffset;
        Vector3 screenPos = mainCamera.WorldToScreenPoint(targetWorldPos);

        if (screenPos.z < 0)
        {
            canvasGroup.alpha = 0f;
        }
        else
        {
            screenPos.z = 0;
            transform.position = screenPos;
        }
    }
}