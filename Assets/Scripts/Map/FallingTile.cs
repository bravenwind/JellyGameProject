using System.Collections;
using UnityEngine;

public class FallingTile : MonoBehaviour
{
    private Coroutine _idleCoroutine;
    private Vector3 _originalPos;
    private float _phase;
    private bool _initialized;

    // 흔들림 대상 (렌더러의 자식 transform). collider는 root에 두고 시각만 흔들어서
    // 위에 올라간 AI가 튕기지 않게 함. 렌더러가 root에 직접 붙어있으면 root를 그대로 흔듦.
    private Transform _shakeTransform;
    private Vector3 _shakeOrigin;

    private void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        _originalPos = transform.localPosition;
        _phase = (_originalPos.x * 12.9898f + _originalPos.z * 78.233f) % (Mathf.PI * 2f);

        Renderer rend = GetComponentInChildren<Renderer>();
        if (rend != null && rend.transform != transform)
        {
            _shakeTransform = rend.transform;
            _shakeOrigin = _shakeTransform.localPosition;
        }
        else
        {
            _shakeTransform = transform;
            _shakeOrigin = _originalPos;
        }
    }

    public void StartIdleShake()
    {
        EnsureInit();
        if (_idleCoroutine != null) return;
        _idleCoroutine = StartCoroutine(IdleShakeRoutine());
    }

    public void StartFall(float warningDuration, float fallDuration, float fallDistance, float delay)
    {
        EnsureInit();
        StartCoroutine(FallRoutine(warningDuration, fallDuration, fallDistance, delay));
    }

    private IEnumerator IdleShakeRoutine()
    {
        float elapsed = 0f;
        const float intensity = 0.06f;

        while (true)
        {
            Vector3 shake = new Vector3(
                Mathf.Sin(elapsed * 25f + _phase) * intensity,
                Mathf.Abs(Mathf.Sin(elapsed * 35f + _phase)) * intensity * 0.2f,
                Mathf.Sin(elapsed * 31f + _phase) * intensity
            );
            _shakeTransform.localPosition = _shakeOrigin + shake;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator FallRoutine(float warningDuration, float fallDuration,
        float fallDistance, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (_idleCoroutine != null)
        {
            StopCoroutine(_idleCoroutine);
            _idleCoroutine = null;
            transform.localPosition = _originalPos;
        }

        Renderer rend = GetComponentInChildren<Renderer>();
        Color originalColor = Color.white;
        if (rend != null)
            originalColor = rend.material.color;

        // 경고 단계: 빨갛게 변하면서 더 격하게 흔들림 (시각만)
        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            float t = elapsed / warningDuration;
            float intensity = Mathf.Lerp(0.12f, 0.4f, t);
            Vector3 shake = new Vector3(
                Mathf.Sin(elapsed * 37f + _phase) * intensity,
                Mathf.Abs(Mathf.Sin(elapsed * 53f + _phase)) * intensity * 0.25f,
                Mathf.Sin(elapsed * 43f + _phase) * intensity
            );
            _shakeTransform.localPosition = _shakeOrigin + shake;

            if (rend != null)
                rend.material.color = Color.Lerp(originalColor, new Color(1f, 0.25f, 0.15f), t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        _shakeTransform.localPosition = _shakeOrigin;

        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = false;

        // 낙하 단계: 전체 transform을 Y축으로 가속 하강
        elapsed = 0f;
        while (elapsed < fallDuration)
        {
            float t = elapsed / fallDuration;
            float fallY = fallDistance * (t * t);
            transform.localPosition = _originalPos + Vector3.down * fallY;

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = _originalPos + Vector3.down * fallDistance;
        gameObject.SetActive(false);
    }
}
