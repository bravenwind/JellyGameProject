using UnityEngine;
using TMPro;
using System.Collections;

public class LevelUpFloater : MonoBehaviour
{
    [Header("텍스트")]
    public string displayText = "Level Up!";
    public float fontSize = 6f;
    public Color textColor = new Color(1f, 0.9f, 0.15f);
    public Color outlineColor = new Color(0.55f, 0.25f, 0f);

    [Header("애니메이션")]
    public float duration = 1.2f;
    public float floatHeight = 2.0f;
    public float startHeight = 2.0f;

    private TextMeshPro _tmp;
    private Transform _textTf;
    private Transform _parentTf;
    private Camera _cam;
    private Coroutine _routine;

    private void Awake()
    {
        _parentTf = transform;
        _cam = Camera.main;
        EnsureTextObject();
    }

    /// <summary>
    /// 텍스트 오브젝트를 생성한다. 이미 살아있으면 그대로 둔다.
    /// 외부 요인으로 파괴된 경우(MissingReference) 재생성하여 안전하게 복구한다.
    /// 텍스트는 플로터 자신의 자식으로 두어, 플레이어 루트 자식을 정리하는 로직 등에
    /// 의해 파괴되지 않도록 한다.
    /// </summary>
    private void EnsureTextObject()
    {
        if (_textTf != null && _tmp != null) return;

        if (_parentTf == null) _parentTf = transform;

        var go = new GameObject("LevelUpText");
        go.transform.SetParent(transform, false);
        _textTf = go.transform;

        _tmp = go.AddComponent<TextMeshPro>();
        _tmp.text = displayText;
        _tmp.fontSize = fontSize;
        _tmp.alignment = TextAlignmentOptions.Center;
        _tmp.fontStyle = FontStyles.Bold;
        _tmp.color = textColor;
        _tmp.outlineWidth = 0.3f;
        _tmp.outlineColor = outlineColor;
        _tmp.sortingOrder = 100;
        _tmp.rectTransform.sizeDelta = new Vector2(5f, 2f);

        go.SetActive(false);
    }

    public void Play()
    {
        // 파괴되었을 수 있으므로 재생성 보장 후 실행
        EnsureTextObject();
        if (_textTf == null || _tmp == null) return;

        if (_routine != null)
            StopCoroutine(_routine);
        _routine = StartCoroutine(AnimateRoutine());
    }

    private IEnumerator AnimateRoutine()
    {
        if (_textTf == null || _tmp == null) yield break;
        _textTf.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // 애니메이션 도중 부모(플레이어)나 텍스트가 파괴되면 즉시 중단
            if (_textTf == null || _tmp == null) { _routine = null; yield break; }

            if (_cam == null) _cam = Camera.main;

            float parentScale = _parentTf != null
                ? Mathf.Max(_parentTf.localScale.x, 0.01f) : 1f;

            float worldY = startHeight * parentScale + floatHeight * EaseOutCubic(t);
            _textTf.localPosition = new Vector3(0f, worldY, 0f);

            if (_cam != null)
                _textTf.rotation = Quaternion.LookRotation(
                    _textTf.position - _cam.transform.position);

            _textTf.localScale = Vector3.one / parentScale;

            float scale;
            if (t < 0.12f)
                scale = Mathf.Lerp(0f, 1.35f, t / 0.12f);
            else if (t < 0.25f)
                scale = Mathf.Lerp(1.35f, 1f, (t - 0.12f) / 0.13f);
            else
                scale = 1f;
            _textTf.localScale *= scale;

            Color c = textColor;
            if (t < 0.1f)
                c.a = t / 0.1f;
            else if (t < 0.55f)
                c.a = 1f;
            else
                c.a = Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
            _tmp.color = c;

            yield return null;
        }

        if (_textTf != null)
            _textTf.gameObject.SetActive(false);
        _routine = null;
    }

    private static float EaseOutCubic(float x)
    {
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }
}
