using UnityEngine;
using TMPro;
using System;
using System.Collections;

/// <summary>
/// 성장(젤리 흡수 / 배트 적중) 시 떠오르는 "Level Up!" 팝업 1개.
/// 이 스크립트는 '팝업 프리팹'에 부착해 사용한다(인스턴스 = 프리팹 1개).
/// 인스턴스 관리는 LevelUpFloaterPool(컨테이너)이 담당하며,
/// Play() 호출 시 활성화 → 애니메이션 → 비활성화하고 onComplete로 풀에 반환된다.
/// </summary>
[DisallowMultipleComponent]
public class LevelUpFloater : MonoBehaviour
{
    [Header("텍스트 (프리팹에 TMP를 미리 붙여두면 그걸 사용)")]
    public TextMeshPro tmp;
    public string displayText = "Level Up!";
    public float fontSize = 6f;
    public Color textColor = new Color(1f, 0.9f, 0.15f);
    public Color outlineColor = new Color(0.55f, 0.25f, 0f);

    [Header("애니메이션")]
    public float duration = 1.2f;
    public float floatHeight = 2.0f;
    public float startHeight = 2.0f;
    [Tooltip("등장 위치 좌우 흩뿌림(부모 로컬 기준). 동시에 여러 개 뜰 때 겹침 방지.")]
    public float spreadX = 0.6f;

    private Transform _scaleRef;   // 텍스트 크기 상쇄 기준(플레이어 루트)
    private Camera _cam;
    private Action<LevelUpFloater> _onComplete;
    private Coroutine _routine;

    private void Awake()
    {
        EnsureText();
        gameObject.SetActive(false);   // 풀에서 꺼낼 때 활성화된다
    }

    /// <summary>프리팹에 TMP가 없으면 런타임에 생성하고 스타일을 적용한다.</summary>
    private void EnsureText()
    {
        if (tmp == null) tmp = GetComponentInChildren<TextMeshPro>(true);
        if (tmp == null) tmp = gameObject.AddComponent<TextMeshPro>();

        tmp.text = displayText;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = textColor;
        tmp.outlineWidth = 0.3f;
        tmp.outlineColor = outlineColor;
        tmp.sortingOrder = 100;
        if (tmp.rectTransform != null)
            tmp.rectTransform.sizeDelta = new Vector2(5f, 2f);
    }

    /// <summary>풀에서 호출. scaleRef는 크기 상쇄 기준(플레이어 루트), onComplete는 반환 콜백.</summary>
    public void Play(Transform scaleRef, Action<LevelUpFloater> onComplete)
    {
        _scaleRef = scaleRef;
        _onComplete = onComplete;
        _cam = Camera.main;
        EnsureText();

        gameObject.SetActive(true);
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(AnimateRoutine());
    }

    private IEnumerator AnimateRoutine()
    {
        // 동시에 여러 개가 뜰 때 겹치지 않도록 좌우로 약간 흩뿌린다.
        float offsetX = UnityEngine.Random.Range(-spreadX, spreadX);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (tmp == null) break;

            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            if (_cam == null) _cam = Camera.main;

            float parentScale = _scaleRef != null
                ? Mathf.Max(_scaleRef.localScale.x, 0.01f) : 1f;

            // 위치: 부모 로컬 기준 원시 오프셋(스케일 곱 X). NameTagBillboard와 동일 패턴.
            float localY = startHeight + floatHeight * EaseOutCubic(t);
            transform.localPosition = new Vector3(offsetX, localY, 0f);

            // 빌보드: 카메라 정면
            if (_cam != null)
                transform.rotation = Quaternion.LookRotation(
                    transform.position - _cam.transform.position);

            // 크기: 부모 스케일 상쇄 → 항상 일정 + 등장 팝 연출
            float pop;
            if (t < 0.12f) pop = Mathf.Lerp(0f, 1.35f, t / 0.12f);
            else if (t < 0.25f) pop = Mathf.Lerp(1.35f, 1f, (t - 0.12f) / 0.13f);
            else pop = 1f;
            transform.localScale = Vector3.one / parentScale * pop;

            // 알파: 페이드 인 → 유지 → 페이드 아웃
            Color c = textColor;
            if (t < 0.1f) c.a = t / 0.1f;
            else if (t < 0.55f) c.a = 1f;
            else c.a = Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
            tmp.color = c;

            yield return null;
        }

        _routine = null;
        gameObject.SetActive(false);
        _onComplete?.Invoke(this);   // 풀에 반환
    }

    private static float EaseOutCubic(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
}
