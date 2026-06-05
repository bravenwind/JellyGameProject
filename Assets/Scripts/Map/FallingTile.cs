using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

public class FallingTile : MonoBehaviour
{
    [Header("디버그")]
    [Tooltip("AwakePhysicsOnTile이 사용하는 OverlapBox 범위를 Scene 뷰에 시각화")]
    public bool drawOverlapGizmo = true;

    [Tooltip("OverlapBox 높이 (타일 위로 몇 미터까지 검사할지)")]
    public float overlapBoxHeight = 20f;

    private Coroutine _idleCoroutine;
    private Vector3 _originalPos;
    private float _phase;
    private bool _initialized;
    private Collider _collider;
    private NavMeshObstacle _navObstacle;

    private Transform _shakeTransform;
    private Vector3 _shakeOrigin;

    // 디버그용: 최근 AwakePhysicsOnTile이 잡은 콜라이더들을 일정 시간 강조 표시
    private Collider[] _lastOverlapResult;
    private float _lastOverlapTime = -999f;

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

            // 2. NavMeshObstacle(Carving)을 켜서 이 타일 위치의 NavMesh에 구멍을 뚫는다.
            //    NavMesh는 게임 시작 시 한 번만 베이크되므로, 타일이 사라져도 NavMesh 표면은 그대로 남아
            //    AI가 빈 공간을 걸어다니는 원인이 된다. Carving은 런타임에 NavMesh를 동적으로 잘라준다.
            CarveNavMesh(_collider.bounds.size);

            // 3. 낙하하는 타일 콜라이더 비활성화 (플레이어 밀림/끼임 버그 방지)
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
        // halfExtents는 전체 크기의 절반
        // Y축은 overlapBoxHeight 만큼 타일 위 공간을 검사 (반지름이므로 절반)
        Vector3 halfExtents = new Vector3(colliderSize.x * 0.5f, overlapBoxHeight * 0.5f, colliderSize.z * 0.5f);

        // 박스 중심을 타일 표면 위쪽에 배치 (타일 위 공간만 스캔)
        Vector3 boxCenter = transform.position + new Vector3(0f, halfExtents.y, 0f);

        // 배경 오브젝트 + AI (Player/Edible 레이어) 모두 감지
        int mask = DataManager.Instance.objectLayerMask
            | (1 << LayerMask.NameToLayer("Player"))
            | (1 << LayerMask.NameToLayer("Edible"));

        Collider[] OverlappedCols = Physics.OverlapBox(boxCenter, halfExtents, transform.rotation, mask);

        // 디버그: 시각화용 기록 + Scene 뷰에 라인으로 5초간 그림
        _lastOverlapResult = OverlappedCols;
        _lastOverlapTime = Time.time;
        if (drawOverlapGizmo)
        {
            DebugDrawOverlapBox(boxCenter, halfExtents, transform.rotation, Color.red, 5f);
            Debug.Log($"[FallingTile] {name} AwakePhysicsOnTile: {OverlappedCols.Length}개 콜라이더 감지");
            foreach (var c in OverlappedCols)
            {
                if (c == null || c == _collider) continue;
                Rigidbody r = c.GetComponentInParent<Rigidbody>();
                Debug.Log($"  └ {c.name} layer={LayerMask.LayerToName(c.gameObject.layer)} tag={c.tag} rb={(r != null ? r.name + "(kin=" + r.isKinematic + ")" : "없음")}");
            }
        }

        foreach (var col in OverlappedCols)
        {
            if (col == _collider) continue;

            Rigidbody rb = col.GetComponentInParent<Rigidbody>();

            if (rb == null)
            {
                AIPlayerMovement aiBot = col.GetComponentInParent<AIPlayerMovement>();
                if (aiBot != null && !aiBot.IsEliminated && !aiBot.IsBeingAbsorbed)
                {
                    PhotonView aiPV = aiBot.GetComponent<PhotonView>();
                    if (aiPV != null && !PhotonNetwork.IsMasterClient) continue;

                    DisableAIOnObject(aiBot.gameObject);

                    CharacterController cc = aiBot.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;

                    rb = aiBot.gameObject.AddComponent<Rigidbody>();
                    rb.useGravity = true;
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                }
                else
                {
                    continue;
                }
            }

            if (rb.GetComponent<NetworkPlayerSync>() != null) continue;

            PhotonView netView = rb.GetComponent<PhotonView>();
            if (netView != null && !PhotonNetwork.IsMasterClient) continue;

            DisableAIOnObject(rb.gameObject);

            CharacterController ccOnRb = rb.GetComponent<CharacterController>();
            if (ccOnRb != null) ccOnRb.enabled = false;

            if (rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.AddTorque(new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)), ForceMode.Impulse);
            }
        }
    }

    private void CarveNavMesh(Vector3 colliderSize)
    {
        _navObstacle = gameObject.AddComponent<NavMeshObstacle>();
        _navObstacle.shape = NavMeshObstacleShape.Box;
        _navObstacle.size = new Vector3(colliderSize.x, colliderSize.y + 1f, colliderSize.z);
        _navObstacle.center = Vector3.up * (colliderSize.y * 0.5f);
        _navObstacle.carving = true;
        _navObstacle.carveOnlyStationary = false;
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

    // ─────────────────────────────────────────────────────────
    // 디버그 시각화
    // ─────────────────────────────────────────────────────────

    /// <summary>OverlapBox 영역을 Debug.DrawLine으로 표시 (Scene 뷰, duration초 동안)</summary>
    private static void DebugDrawOverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation, Color color, float duration)
    {
        Vector3[] corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3 local = new Vector3(
                (i & 1) == 0 ? -halfExtents.x : halfExtents.x,
                (i & 2) == 0 ? -halfExtents.y : halfExtents.y,
                (i & 4) == 0 ? -halfExtents.z : halfExtents.z);
            corners[i] = center + rotation * local;
        }
        // 12개 모서리
        int[,] edges = new int[,] {
            {0,1},{2,3},{4,5},{6,7},
            {0,2},{1,3},{4,6},{5,7},
            {0,4},{1,5},{2,6},{3,7}
        };
        for (int i = 0; i < 12; i++)
            Debug.DrawLine(corners[edges[i, 0]], corners[edges[i, 1]], color, duration, false);
    }

    /// <summary>Scene 뷰 항상 표시: 이 타일의 OverlapBox 예정 범위를 녹색 와이어로 표시</summary>
    private void OnDrawGizmos()
    {
        if (!drawOverlapGizmo) return;

        Collider col = _collider != null ? _collider : GetComponent<Collider>();
        if (col == null) return;

        Vector3 size = col.bounds.size;
        Vector3 halfExtents = new Vector3(size.x * 0.5f, overlapBoxHeight * 0.5f, size.z * 0.5f);
        Vector3 center = transform.position + new Vector3(0f, halfExtents.y, 0f);

        // 최근에 트리거된 박스는 빨강, 아니면 녹색
        bool recentlyTriggered = Time.time - _lastOverlapTime < 5f;
        Color baseColor = recentlyTriggered ? Color.red : Color.green;

        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);

        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);

        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.1f);
        Gizmos.DrawCube(Vector3.zero, halfExtents * 2f);

        Gizmos.matrix = prev;
    }
}