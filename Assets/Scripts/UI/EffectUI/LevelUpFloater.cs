using UnityEngine;
using TMPro;

public class LevelUpFloater : MonoBehaviour
{
    private Transform _target;
    private TextMeshProUGUI _text;
    private CanvasGroup _group;
    private RectTransform _rect;
    private float _elapsed;
    private float _baseY;

    private const float DURATION = 1.2f;
    private const float FLOAT_DISTANCE = 120f;

    private static Transform _cachedCanvasTransform;

    public static void Spawn(Transform target)
    {
        if (target == null) return;

        Transform canvasT = FindCanvasTransform();
        if (canvasT == null) return;

        var go = new GameObject("LevelUpFloater");
        go.transform.SetParent(canvasT, false);
        var floater = go.AddComponent<LevelUpFloater>();
        floater.Init(target);
    }

    private static Transform FindCanvasTransform()
    {
        if (_cachedCanvasTransform != null) return _cachedCanvasTransform;

        if (UIPoolManager.Instance != null && UIPoolManager.Instance.canvasTransform != null)
        {
            _cachedCanvasTransform = UIPoolManager.Instance.canvasTransform;
            return _cachedCanvasTransform;
        }

        foreach (var c in FindObjectsOfType<Canvas>())
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _cachedCanvasTransform = c.transform;
                return _cachedCanvasTransform;
            }
        }
        return null;
    }

    private void Init(Transform target)
    {
        _target = target;

        _rect = gameObject.AddComponent<RectTransform>();
        _rect.sizeDelta = new Vector2(300, 60);

        _text = gameObject.AddComponent<TextMeshProUGUI>();
        _text.text = "Level Up!";
        _text.fontSize = 42;
        _text.alignment = TextAlignmentOptions.Center;
        _text.fontStyle = FontStyles.Bold;
        _text.color = new Color(1f, 0.9f, 0.15f);
        _text.enableAutoSizing = false;
        _text.raycastTarget = false;
        _text.outlineWidth = 0.25f;
        _text.outlineColor = new Color(0.55f, 0.25f, 0f);

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.alpha = 0f;

        transform.localScale = Vector3.zero;

        UpdateScreenPosition();
        _baseY = transform.position.y;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = _elapsed / DURATION;

        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        UpdateScreenPosition();
        float floatOffset = FLOAT_DISTANCE * EaseOutCubic(t);
        transform.position = new Vector3(transform.position.x, _baseY + floatOffset, 0f);

        float scale;
        if (t < 0.12f)
            scale = Mathf.Lerp(0f, 1.35f, t / 0.12f);
        else if (t < 0.25f)
            scale = Mathf.Lerp(1.35f, 1f, (t - 0.12f) / 0.13f);
        else
            scale = 1f;
        transform.localScale = Vector3.one * scale;

        if (t < 0.1f)
            _group.alpha = t / 0.1f;
        else if (t < 0.55f)
            _group.alpha = 1f;
        else
            _group.alpha = Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
    }

    private void UpdateScreenPosition()
    {
        if (_target == null || Camera.main == null) return;
        float height = _target.localScale.x * 1.5f;
        Vector3 worldPos = _target.position + Vector3.up * height;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
        if (screenPos.z > 0)
        {
            _baseY = screenPos.y;
            transform.position = new Vector3(screenPos.x, screenPos.y, 0f);
        }
    }

    private static float EaseOutCubic(float x)
    {
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }
}
