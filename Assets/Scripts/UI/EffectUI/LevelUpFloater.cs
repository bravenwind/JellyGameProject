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
        _parentTf = transform.parent;
        _cam = Camera.main;

        var go = new GameObject("LevelUpText");
        go.transform.SetParent(transform.parent, false);
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
        if (_routine != null)
            StopCoroutine(_routine);
        _routine = StartCoroutine(AnimateRoutine());
    }

    private IEnumerator AnimateRoutine()
    {
        _textTf.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

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

        _textTf.gameObject.SetActive(false);
        _routine = null;
    }

    private static float EaseOutCubic(float x)
    {
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }
}
