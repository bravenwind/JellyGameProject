using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

public class FallingTile : MonoBehaviour
{
    [Header("디버그")]
    [Tooltip("AwakePhysicsOnTile이 사용하는 OverlapBox 범위를 Scene 뷰에 시각화")]
    public bool drawOverlapGizmo = false;   // 빌드에서 붕괴 때마다 Debug.Log가 쏟아지지 않도록 기본 off (G3)

    [Tooltip("OverlapBox 높이 (타일 위로 몇 미터까지 검사할지)")]
    public float overlapBoxHeight = 20f;

    // 경고 단계 색 변경용 셰이더 프로퍼티(URP=_BaseColor / 빌트인=_Color). MaterialPropertyBlock으로
    // 칠하면 .material처럼 인스턴스를 복제하지 않아 배칭이 유지된다. (G3)
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // TileCollapseManager가 붕괴 예약 시 채워주는 그리드 좌표. carve 시점에 '허공'으로 마킹할 때 사용.
    [HideInInspector] public int GridX = -1;
    [HideInInspector] public int GridZ = -1;

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

        // 색 변경은 .material(인스턴스 복제) 대신 MaterialPropertyBlock으로 → 붕괴 타일마다 머티리얼
        // 사본이 생겨 배칭이 깨지고 메모리가 늘던 문제를 막는다. 읽기도 sharedMaterial로 한다. (G3)
        Renderer rend = GetComponentInChildren<Renderer>();
        Color originalColor = Color.white;
        int colorPropId = ColorId;
        MaterialPropertyBlock mpb = null;
        if (rend != null && rend.sharedMaterial != null)
        {
            colorPropId = rend.sharedMaterial.HasProperty(BaseColorId) ? BaseColorId : ColorId;
            if (rend.sharedMaterial.HasProperty(colorPropId))
                originalColor = rend.sharedMaterial.GetColor(colorPropId);
            mpb = new MaterialPropertyBlock();
        }

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

            if (mpb != null)
            {
                rend.GetPropertyBlock(mpb);
                mpb.SetColor(colorPropId, Color.Lerp(originalColor, new Color(1f, 0.25f, 0.15f), t));
                rend.SetPropertyBlock(mpb);
            }

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

            // 2-1. 발판이 실제로 사라지는 시점이므로 이 칸을 '허공'으로 표시한다.
            //      AI가 잔존 NavMesh 위에 떠 있게 되는 경우를 감지/복구하는 데 쓰인다.
            if (GridX >= 0 && GridZ >= 0)
                TileCollapseManager.Instance?.MarkCellCollapsed(GridX, GridZ);

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

        // 배경 오브젝트 + AI (Player/Edible 레이어) 모두 감지.
        // [X4/G8] 여기는 붕괴 코루틴(FallRoutine) 한복판이라 NRE가 나면 이 타일만
        // 조용히 좀비(흔들리다 멈춘 채 영구 잔존, carve/IsOverVoid 미반영)가 된다 —
        // 싱글톤/레이어는 없으면 건너뛰는 식으로 방어한다.
        // (NameToLayer가 -1이면 C# 시프트 마스킹으로 1<<-1 == 1<<31이 되어
        //  엉뚱한 31번 레이어가 마스크에 섞이는 것도 함께 차단)
        var dm = DataManager.Instance;
        int mask = dm != null ? dm.objectLayerMask.value : 0;
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) mask |= 1 << playerLayer;
        int edibleLayer = LayerMask.NameToLayer("Edible");
        if (edibleLayer >= 0) mask |= 1 << edibleLayer;

        Collider[] OverlappedCols = Physics.OverlapBox(boxCenter, halfExtents, transform.rotation, mask);

        // 디버그: 시각화용 기록 + Scene 뷰에 라인으로 5초간 그림
        _lastOverlapResult = OverlappedCols;
        _lastOverlapTime = Time.time;
        if (drawOverlapGizmo)
        {
            DebugDrawOverlapBox(boxCenter, halfExtents, transform.rotation, Color.red, 5f);
#if UNITY_EDITOR
            // 로그는 에디터에서만. 빌드에선 컴파일 자체가 빠져 프레임 스파이크/오버헤드 없음. (G3)
            Debug.Log($"[FallingTile] {name} AwakePhysicsOnTile: {OverlappedCols.Length}개 콜라이더 감지");
            foreach (var c in OverlappedCols)
            {
                if (c == null || c == _collider) continue;
                Rigidbody r = c.GetComponentInParent<Rigidbody>();
                Debug.Log($"  └ {c.name} layer={LayerMask.LayerToName(c.gameObject.layer)} tag={c.tag} rb={(r != null ? r.name + "(kin=" + r.isKinematic + ")" : "없음")}");
            }
#endif
        }

        foreach (var col in OverlappedCols)
        {
            if (col == _collider) continue;

            Rigidbody rb = col.GetComponentInParent<Rigidbody>();

            if (col.GetComponent<Milk>() != null)
            {
                rb = transform.root.GetComponent<Rigidbody>();
            }

            if (rb == null)
            {
                AIPlayerMovement aiBot = col.GetComponentInParent<AIPlayerMovement>();
                if (aiBot != null && !aiBot.IsOutOfPlay)
                {
                    PhotonView aiPV = aiBot.GetComponent<PhotonView>();
                    if (aiPV != null && !PhotonNetwork.IsMasterClient) continue;

                    DisableAIOnObject(aiBot.gameObject);

                    CharacterController cc = aiBot.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;

                    rb = aiBot.gameObject.AddComponent<Rigidbody>();
                    MakeCollidersDynamicSafe(rb);
                    rb.useGravity = true;
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                }
                else
                {
                    continue;
                }
            }

            // ★ [LAN 이식] 사람 플레이어는 발판이 건드리지 않는다.
            //
            //   원래 이 줄이 NetworkPlayerSync로 사람을 걸러냈다. 그 컴포넌트를 걷어낸 뒤로
            //   <b>사람도 일반 물체 취급</b>이 되어, 발판이 CharacterController를 꺼버리고
            //   Rigidbody를 붙였다. 그런데 PlayerMovement는 계속 돌기 때문에
            //   "CharacterController.Move called on inactive controller"가 쏟아진다.
            //   (사람의 낙하는 PlayerMovement/초콜릿 경로가 따로 처리한다)
            if (rb.GetComponentInParent<JellyNet.LanPlayerState>() != null) continue;

            // 원격 오브젝트는 소유자 쪽에서만 물리를 돌린다
            JellyNet.NetIdentity nid = rb.GetComponentInParent<JellyNet.NetIdentity>();
            if (nid != null && !nid.IsSimulatedHere) continue;

            PhotonView netView = rb.GetComponent<PhotonView>();
            if (netView != null && !PhotonNetwork.IsMasterClient) continue;

            DisableAIOnObject(rb.gameObject);

            CharacterController ccOnRb = rb.GetComponent<CharacterController>();
            if (ccOnRb != null) ccOnRb.enabled = false;

            MakeCollidersDynamicSafe(rb);

            if (rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.AddTorque(new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)), ForceMode.Impulse);
            }

            // 이미 non-kinematic이던 물체(NavMeshAgent가 몰던 젤리 등)는 위 분기를 타지 않아
            // 중력이 꺼진 채 남았다. 그리고 가만히 서 있던 Rigidbody는 잠들어 있는데,
            // 바닥 콜라이더를 끄는 건 충돌 이벤트가 아니라서 저절로 깨어나지 않는다.
            // → 발판이 사라져도 공중에 그대로 멈춰 있게 된다.
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.WakeUp();
        }
    }

    /// <summary>
    /// 물리를 켜기 전에 콜라이더를 정리한다.
    ///
    /// ★ 왜 필요한가
    ///   유니티는 <b>오목한(concave) MeshCollider를 동적 Rigidbody에 허용하지 않는다.</b>
    ///   그대로 두면 발판이 무너질 때마다 이 경고가 쏟아진다:
    ///
    ///     "Concave Mesh Colliders are not supported when used with
    ///      dynamic Rigidbody GameObjects."
    ///
    ///   맵의 밀크·소품은 ProBuilder 메시라 대부분 오목하다. 그래서 발판 위에 있던
    ///   소품이 떨어지려는 순간 걸린다. 물리가 아예 안 붙어 소품이 공중에 뜬 채 남기도 한다.
    ///
    ///   부서져 떨어지는 잔해에는 정밀한 충돌이 필요 없으므로 볼록 껍질로 바꾼다.
    ///   (원래 모양은 이미 화면에서 사라지는 중이라 티가 나지 않는다)
    /// </summary>
    private static void MakeCollidersDynamicSafe(Rigidbody rb)
    {
        MeshCollider[] mcs = rb.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < mcs.Length; i++)
        {
            if (mcs[i] == null || mcs[i].convex) continue;
            mcs[i].convex = true;
        }
    }

    private void CarveNavMesh(Vector3 colliderSize)
    {
        // [중요] NavMeshObstacle을 '낙하하는 타일 자신'에 붙이면, 타일이 아래로 떨어질 때
        //        carving 장애물도 함께 내려가 NavMesh 표면에서 멀어진다. 그러면 뚫었던 구멍이
        //        다시 닫혀, 바닥은 없는데 NavMesh 표면만 되살아나는 '유령 NavMesh'가 생긴다.
        //        → AI가 무너진 타일 위(허공)를 걸어다니거나, 그 위로 튕겨 올라가 제자리에서
        //          빙빙 도는 현상, 맵 꼭짓점에 박혀 살아남는 현상의 근본 원인.
        //
        //        해결: 타일과 분리된 '고정' 오브젝트에 carving 장애물을 부착해 구멍을 영구 유지한다.
        //        colliderSize는 월드 단위(bounds.size)이므로, 부모 스케일에 왜곡되지 않도록
        //        부모 없이(루트) 배치해 lossyScale=1을 보장한다. (씬 전환 시 자동 정리)
        GameObject carveObj = new GameObject($"NavCarve_{name}");
        carveObj.transform.position = transform.position;
        carveObj.transform.rotation = transform.rotation;

        _navObstacle = carveObj.AddComponent<NavMeshObstacle>();
        _navObstacle.shape = NavMeshObstacleShape.Box;
        _navObstacle.size = new Vector3(colliderSize.x, colliderSize.y + 1f, colliderSize.z);
        _navObstacle.center = Vector3.up * (colliderSize.y * 0.5f);
        _navObstacle.carving = true;
        // 고정 오브젝트라 한 번만 carving되고 이후 재계산 비용이 없다.
        // false로 두면 stationary 타이머(~0.5초)를 기다리지 않고 즉시 구멍을 뚫어,
        // 그 사이 AI가 허공으로 튕기는 타이밍 공백을 없앤다.
        _navObstacle.carveOnlyStationary = false;

        // 한 판 동안 무한 누적되는 carve 오브젝트를 매니저가 소유해 라운드 종료 시 일괄 정리한다. (G7)
        TileCollapseManager.Instance?.RegisterCarveObject(carveObj);
    }

    // AI 스크립트를 먼저 끄지 않으면 다음 프레임에 스스로 agent를 다시 켠다
    // (JellyAgentAI가 구동 권한을 잡으면 agent를 켠다) → 발판이 사라진 자리를 계속 걸어다닌다.
    // agent는 자식에 달린 프리팹도 있어 GetComponentsInChildren으로 훑는다.
    private static void DisableAIOnObject(GameObject obj)
    {
        foreach (var wandering in obj.GetComponentsInChildren<WanderingAI>(true))
            wandering.enabled = false;

        foreach (var patrol in obj.GetComponentsInChildren<AIWaypointPatrol>(true))
            patrol.enabled = false;

        foreach (var navAgent in obj.GetComponentsInChildren<NavMeshAgent>(true))
            navAgent.enabled = false;
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