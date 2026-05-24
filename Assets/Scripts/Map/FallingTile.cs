using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class FallingTile : MonoBehaviour
{
    private Coroutine _idleCoroutine;
    private Vector3 _originalPos;
    private float _phase;
    private bool _initialized;
    private Collider _collider;

    private Transform _shakeTransform;
    private Vector3 _shakeOrigin;

    private void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        _originalPos = transform.localPosition;
        _phase = (_originalPos.x * 12.9898f + _originalPos.z * 78.233f) % (Mathf.PI * 2f);

        _collider = GetComponent<Collider>(); // AwakePhysics에서 쓰기 위해 미리 캐싱

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

        // ── 붕괴 직전 타이밍 (여기서 물리를 깨우고 바닥을 치웁니다) ──
        _shakeTransform.localPosition = _shakeOrigin;

        if (_collider != null)
        {
            // 1. 위에 있는 오브젝트들 물리 켜기 (HalfExtents 공식 적용)
            AwakePhysicsOnTile(_collider.bounds.size);

            // 2. [추가] 낙하하는 타일 콜라이더 비활성화 (플레이어 밀림/끼임 버그 방지)
            _collider.enabled = false;
        }

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

    private void AwakePhysicsOnTile(Vector3 colliderSize)
    {
        // 💡 핵심 수정: halfExtents는 전체 크기의 절반이어야 함!
        // Y축 높이는 타일 위로 5m만 검사하도록 세팅 (반지름이므로 2.5f)
        Vector3 halfExtents = new Vector3(colliderSize.x * 0.5f, 10f, colliderSize.z * 0.5f);

        // 💡 박스의 중심점을 타일 표면에서 약간 위쪽(Y축 +2.5m)으로 배치해서 딱 타일 위의 공간만 스캔
        Vector3 boxCenter = transform.position + new Vector3(0f, halfExtents.y, 0f);

        // 배경 오브젝트 + AI (Player/Edible 레이어) 모두 감지
        int mask = DataManager.Instance.objectLayerMask
            | (1 << LayerMask.NameToLayer("Player"))
            | (1 << LayerMask.NameToLayer("Edible"));

        Collider[] OverlappedCols = Physics.OverlapBox(boxCenter, halfExtents, transform.rotation, mask);

        foreach (var col in OverlappedCols)
        {
            // 나 자신(타일 콜라이더)이 감지되는 것 방지
            if (col == _collider) continue;

            Rigidbody rb = col.GetComponentInParent<Rigidbody>();
            if (rb == null) continue;

            // 실제 플레이어는 자체 이동 시스템이 있으므로 건드리지 않음
            if (rb.GetComponent<NetworkPlayerSync>() != null) continue;

            // AI 위에 있다면 AI 컴포넌트들을 먼저 꺼서 NavMeshAgent가 물리와 싸우지 않게 함
            DisableAIOnObject(rb.gameObject);

            if (rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.useGravity = true;

                // 탕후루 꼬치가 삐딱하게 떨어지는 역동적인 연출
                rb.AddTorque(new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)), ForceMode.Impulse);
            }
        }
    }

    private static void DisableAIOnObject(GameObject obj)
    {
        var navAgent = obj.GetComponent<NavMeshAgent>();
        if (navAgent != null) navAgent.enabled = false;

        var wandering = obj.GetComponent<WanderingAI>();
        if (wandering != null) wandering.enabled = false;

        var aiPlayer = obj.GetComponent<AIPlayerMovement>();
        if (aiPlayer != null) aiPlayer.enabled = false;
    }
}