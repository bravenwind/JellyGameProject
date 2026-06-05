using UnityEngine;

public class BatDebugVisualizer : MonoBehaviour
{
    [Header("시각화 설정")]
    public bool showInGameView = true;
    public Color arcColor = new Color(1f, 0.4f, 0f, 0.6f);
    public Color arcHitColor = new Color(1f, 0f, 0f, 0.8f);
    public Color rangeColor = new Color(1f, 1f, 0f, 0.3f);

    private static BatDebugVisualizer _instance;

    private Transform _swingOwner;
    private float _swingRange;
    private float _swingHalfArc;
    private float _swingDuration;
    private float _swingElapsed;
    private bool _isSwinging;

    private const int ARC_SEGMENTS = 20;

    private void Awake()
    {
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public static void NotifySwing(Transform owner, float range, float halfArc, float duration)
    {
        if (_instance == null) return;
        _instance._swingOwner = owner;
        _instance._swingRange = range;
        _instance._swingHalfArc = halfArc;
        _instance._swingDuration = duration;
        _instance._swingElapsed = 0f;
        _instance._isSwinging = true;
    }

    private void Update()
    {
        if (!_isSwinging) return;

        _swingElapsed += Time.deltaTime;
        if (_swingElapsed >= _swingDuration)
            _isSwinging = false;

        if (_swingOwner == null)
        {
            _isSwinging = false;
            return;
        }

        if (showInGameView)
            DrawArcLines();
    }

    private void DrawArcLines()
    {
        Vector3 origin = _swingOwner.position + Vector3.up * 0.5f;
        Vector3 forward = _swingOwner.forward;
        float t = Mathf.Clamp01(_swingElapsed / _swingDuration);

        float currentAngle = Mathf.Lerp(-_swingHalfArc, _swingHalfArc, t);
        Vector3 batDir = Quaternion.Euler(0f, currentAngle, 0f) * forward;
        Debug.DrawRay(origin, batDir * _swingRange, arcHitColor);

        for (int i = 0; i <= ARC_SEGMENTS; i++)
        {
            float a = Mathf.Lerp(-_swingHalfArc, _swingHalfArc, (float)i / ARC_SEGMENTS);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
            Debug.DrawRay(origin, dir * _swingRange, rangeColor);
        }

        Vector3 leftDir = Quaternion.Euler(0f, -_swingHalfArc, 0f) * forward;
        Vector3 rightDir = Quaternion.Euler(0f, _swingHalfArc, 0f) * forward;
        Debug.DrawRay(origin, leftDir * _swingRange, arcColor);
        Debug.DrawRay(origin, rightDir * _swingRange, arcColor);

        Vector3 prev = origin + leftDir * _swingRange;
        for (int i = 1; i <= ARC_SEGMENTS; i++)
        {
            float a = Mathf.Lerp(-_swingHalfArc, _swingHalfArc, (float)i / ARC_SEGMENTS);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
            Vector3 point = origin + dir * _swingRange;
            Debug.DrawLine(prev, point, arcColor);
            prev = point;
        }
    }

    private void OnDrawGizmos()
    {
        if (!_isSwinging || _swingOwner == null) return;

        Vector3 origin = _swingOwner.position + Vector3.up * 0.5f;
        Vector3 forward = _swingOwner.forward;

        Gizmos.color = rangeColor;
        Vector3 leftDir = Quaternion.Euler(0f, -_swingHalfArc, 0f) * forward;
        Vector3 rightDir = Quaternion.Euler(0f, _swingHalfArc, 0f) * forward;
        Gizmos.DrawLine(origin, origin + leftDir * _swingRange);
        Gizmos.DrawLine(origin, origin + rightDir * _swingRange);

        Vector3 prev = origin + leftDir * _swingRange;
        for (int i = 1; i <= ARC_SEGMENTS; i++)
        {
            float a = Mathf.Lerp(-_swingHalfArc, _swingHalfArc, (float)i / ARC_SEGMENTS);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
            Vector3 point = origin + dir * _swingRange;
            Gizmos.DrawLine(prev, point);
            prev = point;
        }

        float t = Mathf.Clamp01(_swingElapsed / _swingDuration);
        float currentAngle = Mathf.Lerp(-_swingHalfArc, _swingHalfArc, t);
        Vector3 batDir = Quaternion.Euler(0f, currentAngle, 0f) * forward;
        Gizmos.color = arcHitColor;
        Gizmos.DrawRay(origin, batDir * _swingRange);
    }
}
