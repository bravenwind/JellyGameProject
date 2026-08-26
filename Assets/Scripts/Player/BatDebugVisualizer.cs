using UnityEngine;

public class BatDebugVisualizer : MonoBehaviour
{
    [Header("시각화 설정")]
    [SerializeField] private bool showInGameView = true;
    [SerializeField] private Color arcColor = new Color(1f, 0.4f, 0f, 0.6f);
    [SerializeField] private Color arcHitColor = new Color(1f, 0f, 0f, 0.8f);
    [SerializeField] private Color rangeColor = new Color(1f, 1f, 0f, 0.3f);

    private static BatDebugVisualizer instance;

    private Transform swingOwner;
    private float swingRange;
    private float swingHalfArc;
    private float swingDuration;
    private float swingElapsed;
    private bool isSwinging;

    private const int ARC_SEGMENTS = 20;

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static void NotifySwing(Transform owner, float range, float halfArc, float duration)
    {
        if (instance == null)
            return;
        instance.swingOwner = owner;
        instance.swingRange = range;
        instance.swingHalfArc = halfArc;
        instance.swingDuration = duration;
        instance.swingElapsed = 0f;
        instance.isSwinging = true;
    }

    private void Update()
    {
        if (!isSwinging)
            return;

        swingElapsed += Time.deltaTime;
        if (swingElapsed >= swingDuration)
            isSwinging = false;

        if (swingOwner == null)
        {
            isSwinging = false;
            return;
        }

        if (showInGameView)
            DrawArcLines();
    }

    private void DrawArcLines()
    {
        Vector3 origin = swingOwner.position + Vector3.up * 0.5f;
        Vector3 forward = swingOwner.forward;
        float t = Mathf.Clamp01(swingElapsed / swingDuration);

        float currentAngle = Mathf.Lerp(-swingHalfArc, swingHalfArc, t);
        Vector3 batDir = Quaternion.Euler(0f, currentAngle, 0f) * forward;
        Debug.DrawRay(origin, batDir * swingRange, arcHitColor);

        for (int i = 0; i <= ARC_SEGMENTS; i++)
        {
            float a = Mathf.Lerp(-swingHalfArc, swingHalfArc, (float)i / ARC_SEGMENTS);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
            Debug.DrawRay(origin, dir * swingRange, rangeColor);
        }

        Vector3 leftDir = Quaternion.Euler(0f, -swingHalfArc, 0f) * forward;
        Vector3 rightDir = Quaternion.Euler(0f, swingHalfArc, 0f) * forward;
        Debug.DrawRay(origin, leftDir * swingRange, arcColor);
        Debug.DrawRay(origin, rightDir * swingRange, arcColor);

        Vector3 prev = origin + leftDir * swingRange;
        for (int i = 1; i <= ARC_SEGMENTS; i++)
        {
            float a = Mathf.Lerp(-swingHalfArc, swingHalfArc, (float)i / ARC_SEGMENTS);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
            Vector3 point = origin + dir * swingRange;
            Debug.DrawLine(prev, point, arcColor);
            prev = point;
        }
    }

    private void OnDrawGizmos()
    {
        if (!isSwinging || swingOwner == null)
            return;

        Vector3 origin = swingOwner.position + Vector3.up * 0.5f;
        Vector3 forward = swingOwner.forward;

        Gizmos.color = rangeColor;
        Vector3 leftDir = Quaternion.Euler(0f, -swingHalfArc, 0f) * forward;
        Vector3 rightDir = Quaternion.Euler(0f, swingHalfArc, 0f) * forward;
        Gizmos.DrawLine(origin, origin + leftDir * swingRange);
        Gizmos.DrawLine(origin, origin + rightDir * swingRange);

        Vector3 prev = origin + leftDir * swingRange;
        for (int i = 1; i <= ARC_SEGMENTS; i++)
        {
            float a = Mathf.Lerp(-swingHalfArc, swingHalfArc, (float)i / ARC_SEGMENTS);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
            Vector3 point = origin + dir * swingRange;
            Gizmos.DrawLine(prev, point);
            prev = point;
        }

        float t = Mathf.Clamp01(swingElapsed / swingDuration);
        float currentAngle = Mathf.Lerp(-swingHalfArc, swingHalfArc, t);
        Vector3 batDir = Quaternion.Euler(0f, currentAngle, 0f) * forward;
        Gizmos.color = arcHitColor;
        Gizmos.DrawRay(origin, batDir * swingRange);
    }
}
