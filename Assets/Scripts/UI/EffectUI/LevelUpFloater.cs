using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 플레이어 루트의 자식으로 붙는 "Level Up!" 플로팅 텍스트 컨테이너.
/// 젤리 흡수 / 배트 적중처럼 성장이 발생할 때 Play()를 호출하면
/// 텍스트 인스턴스(자신의 자식)를 활성화 → UI 애니메이션 → 비활성화(풀 반환)한다.
///
/// 한 번에 여러 번 흡수해 동시에 여러 번 떠야 할 때는, 풀에서 비어있는 인스턴스를
/// 꺼내거나 새 자식을 추가해 각자 독립적으로 애니메이션한다(여러 개 동시 표시 가능).
/// </summary>
public class LevelUpFloater : MonoBehaviour
{
    [Header("프리팹 (선택) — 비우면 런타임에 TextMeshPro 생성")]
    [Tooltip("TextMeshPro(3D)가 붙은 프리팹. 지정하면 이 프리팹을 Instantiate해서 사용한다.")]
    public GameObject textPrefab;

    [Header("텍스트 (런타임 생성 시 스타일)")]
    public string displayText = "Level Up!";
    public float fontSize = 6f;
    public Color textColor = new Color(1f, 0.9f, 0.15f);
    public Color outlineColor = new Color(0.55f, 0.25f, 0f);

    [Header("애니메이션")]
    public float duration = 1.2f;
    public float floatHeight = 2.0f;
    public float startHeight = 2.0f;
    [Tooltip("동시에 여러 개가 뜰 때 좌우로 흩뿌리는 범위(부모 로컬 기준)")]
    public float spreadX = 0.6f;

    // 풀 인스턴스 한 개의 캐시
    private class Floater
    {
        public GameObject go;
        public Transform tf;
        public TextMeshPro tmp;
    }

    private Transform _rootTf;   // 스케일 기준(플레이어 루트). 텍스트 크기 상쇄에 사용.
    private Camera _cam;
    private readonly List<Floater> _all = new List<Floater>();
    private readonly Queue<Floater> _free = new Queue<Floater>();

    private void Awake()
    {
        // 스케일 상쇄 기준은 '플레이어 루트'다. 이 컨테이너 자신(localScale 1)을 쓰면
        // 상쇄가 무력화되어 텍스트가 젤리 크기만큼 커진다. (NameTagBillboard와 동일 패턴)
        _rootTf = transform.parent;
        _cam = Camera.main;
    }

    /// <summary>성장 이펙트 1회 표시. 동시에 여러 번 호출되면 각자 독립적으로 떠오른다.</summary>
    public void Play()
    {
        if (_rootTf == null) _rootTf = transform.parent;

        Floater f = GetFloater();
        if (f == null) return;

        f.go.SetActive(true);
        StartCoroutine(AnimateRoutine(f));
    }

    // ─────────────────────────────────────────────────────────
    // 풀 관리
    // ─────────────────────────────────────────────────────────

    private Floater GetFloater()
    {
        // 비어있는(반환된) 인스턴스 재사용. 단, 외부 요인으로 파괴됐으면 건너뛴다.
        while (_free.Count > 0)
        {
            Floater pooled = _free.Dequeue();
            if (pooled != null && pooled.go != null && pooled.tmp != null)
                return pooled;
        }
        return CreateFloater();
    }

    private Floater CreateFloater()
    {
        GameObject go;
        if (textPrefab != null)
        {
            go = Instantiate(textPrefab, transform);
            go.name = "LevelUpText";
        }
        else
        {
            go = new GameObject("LevelUpText");
            go.transform.SetParent(transform, false);
        }

        var tmp = go.GetComponent<TextMeshPro>();
        if (tmp == null) tmp = go.AddComponent<TextMeshPro>();

        // 런타임 생성/스타일 통일
        tmp.text = displayText;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = textColor;
        tmp.outlineWidth = 0.3f;
        tmp.outlineColor = outlineColor;
        tmp.sortingOrder = 100;
        tmp.rectTransform.sizeDelta = new Vector2(5f, 2f);

        go.SetActive(false);

        var f = new Floater { go = go, tf = go.transform, tmp = tmp };
        _all.Add(f);
        return f;
    }

    // ─────────────────────────────────────────────────────────
    // 애니메이션 (인스턴스별 독립 코루틴 → 동시 표시 가능)
    // ─────────────────────────────────────────────────────────

    private IEnumerator AnimateRoutine(Floater f)
    {
        // 동시에 여러 개가 뜰 때 겹치지 않도록 좌우로 약간 흩뿌린다.
        float offsetX = Random.Range(-spreadX, spreadX);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            // 인스턴스/부모가 파괴되면 즉시 종료
            if (f.tf == null || f.tmp == null) yield break;

            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            if (_cam == null) _cam = Camera.main;

            float parentScale = _rootTf != null
                ? Mathf.Max(_rootTf.localScale.x, 0.01f) : 1f;

            // 위치: 부모(루트) 로컬 기준 원시값(스케일 곱 X). NameTagBillboard와 동일.
            float localY = startHeight + floatHeight * EaseOutCubic(t);
            f.tf.localPosition = new Vector3(offsetX, localY, 0f);

            // 빌보드: 카메라 정면
            if (_cam != null)
                f.tf.rotation = Quaternion.LookRotation(
                    f.tf.position - _cam.transform.position);

            // 크기: 부모 스케일 상쇄 → 항상 일정 + 등장 팝 연출
            float pop;
            if (t < 0.12f)
                pop = Mathf.Lerp(0f, 1.35f, t / 0.12f);
            else if (t < 0.25f)
                pop = Mathf.Lerp(1.35f, 1f, (t - 0.12f) / 0.13f);
            else
                pop = 1f;
            f.tf.localScale = Vector3.one / parentScale * pop;

            // 알파: 페이드 인 → 유지 → 페이드 아웃
            Color c = textColor;
            if (t < 0.1f)
                c.a = t / 0.1f;
            else if (t < 0.55f)
                c.a = 1f;
            else
                c.a = Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
            f.tmp.color = c;

            yield return null;
        }

        // 비활성화 후 풀 반환 → 다음 호출에서 재사용
        if (f.go != null) f.go.SetActive(false);
        _free.Enqueue(f);
    }

    private static float EaseOutCubic(float x)
    {
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }
}
