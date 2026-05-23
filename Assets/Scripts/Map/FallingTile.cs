using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class FallingTile : MonoBehaviour
{
    private Coroutine _idleCoroutine;
    private Vector3 _originalPos;
    private float _phase;
    private bool _initialized;

    private void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        _originalPos = transform.localPosition;
        _phase = (_originalPos.x * 12.9898f + _originalPos.z * 78.233f) % (Mathf.PI * 2f);
    }

    /// <summary>
    /// 떨어지는 차례가 되기 전까지 계속 미세하게 흔들리는 idle shake 시작.
    /// 색깔 변화 없음. StartFall 호출 시 자동 중단됨.
    /// </summary>
    public void StartIdleShake()
    {
        EnsureInit();
        if (_idleCoroutine != null) return;
        _idleCoroutine = StartCoroutine(IdleShakeRoutine());
    }

    public void StartFall(float warningDuration, float fallDuration, float fallDistance, float delay)
    {
        EnsureInit();
        if (_idleCoroutine != null)
        {
            StopCoroutine(_idleCoroutine);
            _idleCoroutine = null;
            transform.localPosition = _originalPos;
        }
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
            transform.localPosition = _originalPos + shake;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator FallRoutine(float warningDuration, float fallDuration,
        float fallDistance, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        Renderer rend = GetComponentInChildren<Renderer>();
        Color originalColor = Color.white;
        if (rend != null)
            originalColor = rend.material.color;

        NavMeshObstacle obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.shape = NavMeshObstacleShape.Box;
        BoxCollider box = GetComponentInChildren<BoxCollider>();
        if (box != null)
        {
            obstacle.size = box.size;
            obstacle.center = box.center;
        }

        // 경고 단계: 빨갛게 변하면서 더 격하게 흔들림
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
            transform.localPosition = _originalPos + shake;

            if (rend != null)
                rend.material.color = Color.Lerp(originalColor, new Color(1f, 0.25f, 0.15f), t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = _originalPos;

        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = false;

        // 원래 자리에 영구 NavMeshObstacle을 남겨서 AI가 빈 공간을 피하게 함
        GameObject navBlock = new GameObject("NavBlock");
        navBlock.transform.SetParent(transform.parent, false);
        navBlock.transform.localPosition = _originalPos;

        NavMeshObstacle permObs = navBlock.AddComponent<NavMeshObstacle>();
        permObs.carving = true;
        permObs.shape = NavMeshObstacleShape.Box;
        permObs.size = obstacle.size;
        permObs.center = obstacle.center;

        Destroy(obstacle);

        // 낙하 단계: Y축으로 가속 하강
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
