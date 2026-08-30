using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using JellyNet;

public class FallingTile : MonoBehaviour
{
    [Header("디버그")]
    [Tooltip("AwakePhysicsOnTile이 사용하는 OverlapBox 범위를 Scene 뷰에 시각화")]
    [SerializeField] private bool drawOverlapGizmo = false;   // 빌드에서 붕괴 때마다 Debug.Log가 쏟아지지 않도록 기본 off (G3)

    [Tooltip("타일 윗면 위로 몇 미터까지 검사할지")]
    [SerializeField] private float overlapBoxHeight = 20f;

    // ★ 왜 아래쪽으로도 파야 하는가 (한 번 이걸로 소품 45개를 잃었다)
    //   예전엔 박스를 transform.position에서 위로만 20m 세웠다. 그런데 타일 콜라이더는
    //   피벗 기준 -0.35 ~ +0.35라, 박스 바닥이 하필 <b>타일 두께 한가운데</b>였다.
    //   맵에는 타일에 파묻히거나 살짝 아래로 내려 배치한 소품이 45개 있었고
    //   (pretzel·bread·caramel·씬 젤리 등, 최대 2.48m 아래) 전부 박스 밖이라
    //   발판이 사라져도 영영 공중에 남았다.
    //   이제 기준을 피벗이 아니라 <b>콜라이더 bounds</b>로 잡고, 아랫면에서 더 파고든다.
    [Tooltip("타일 아랫면 아래로 몇 미터까지 검사할지 (타일에 파묻힌 소품용)")]
    [SerializeField] private float overlapBoxDepth = 3f;

    // 경고 단계 색 변경용. MaterialPropertyBlock으로
    // 칠하면 .material처럼 인스턴스를 복제하지 않아 배칭이 유지된다. (G3)

    // TileCollapseManager가 붕괴 예약 시 채워주는 그리드 좌표. carve 시점에 '허공'으로 마킹할 때 사용.
    //격자 좌표. TileCollapseManager가 타일을 등록하면서 넣어준다
    public int GridX { get; private set; } = -1;
    public int GridZ { get; private set; } = -1;

    public void SetGridPos(int x, int z)
    {
        GridX = x;
        GridZ = z;
    }

    private Coroutine idleCoroutine;
    private Vector3 originalPos;
    private float phase;
    private bool initialized;
    private Collider tileCollider;
    private NavMeshObstacle navObstacle;

    private Transform shakeTransform;
    private Vector3 shakeOrigin;

    // 디버그용: 최근 AwakePhysicsOnTile이 돌아간 시각. 기즈모 강조 표시에 쓴다
    private float lastOverlapTime = -999f;

    private void EnsureInit()
    {
        if (initialized)
            return;
        initialized = true;
        originalPos = transform.localPosition;
        phase = (originalPos.x * 12.9898f + originalPos.z * 78.233f) % (Mathf.PI * 2f);

        tileCollider = GetComponent<Collider>(); // AwakePhysics에서 쓰기 위해 미리 캐싱

        Renderer rend = GetComponentInChildren<Renderer>();
        if (rend != null && rend.transform != transform)
        {
            shakeTransform = rend.transform;
            shakeOrigin = shakeTransform.localPosition;
        }
        else
        {
            shakeTransform = transform;
            shakeOrigin = originalPos;
        }
    }

    public void StartIdleShake()
    {
        EnsureInit();
        if (idleCoroutine != null)
            return;
        idleCoroutine = StartCoroutine(IdleShakeRoutine());
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
                Mathf.Sin(elapsed * 25f + phase) * intensity,
                Mathf.Abs(Mathf.Sin(elapsed * 35f + phase)) * intensity * 0.2f,
                Mathf.Sin(elapsed * 31f + phase) * intensity
            );
            shakeTransform.localPosition = shakeOrigin + shake;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator FallRoutine(float warningDuration, float fallDuration,
        float fallDistance, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (idleCoroutine != null)
        {
            StopCoroutine(idleCoroutine);
            idleCoroutine = null;
            transform.localPosition = originalPos;
        }

        // 색 변경은 .material(인스턴스 복제) 대신 MaterialPropertyBlock으로 → 붕괴 타일마다 머티리얼
        // 사본이 생겨 배칭이 깨지고 메모리가 늘던 문제를 막는다. 읽기도 sharedMaterial로 한다. (G3)
        Renderer rend = GetComponentInChildren<Renderer>();
        Color originalColor = Color.white;
        MaterialPropertyBlock mpb = null;

        if (TileColorProps.HasColor(rend))
        {
            originalColor = rend.sharedMaterial.GetColor(TileColorProps.BaseColorId);
            mpb = new MaterialPropertyBlock();
        }

        // 경고 단계: 빨갛게 변하면서 더 격하게 흔들림 (시각만)
        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            float t = elapsed / warningDuration;
            float intensity = Mathf.Lerp(0.12f, 0.4f, t);
            Vector3 shake = new Vector3(
                Mathf.Sin(elapsed * 37f + phase) * intensity,
                Mathf.Abs(Mathf.Sin(elapsed * 53f + phase)) * intensity * 0.25f,
                Mathf.Sin(elapsed * 43f + phase) * intensity
            );
            shakeTransform.localPosition = shakeOrigin + shake;

            if (mpb != null)
            {
                rend.GetPropertyBlock(mpb);
                mpb.SetColor(TileColorProps.BaseColorId, Color.Lerp(originalColor, new Color(1f, 0.25f, 0.15f), t));
                rend.SetPropertyBlock(mpb);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // ── 붕괴 직전 타이밍 (여기서 물리를 깨우고 바닥을 치웁니다) ──
        shakeTransform.localPosition = shakeOrigin;

        if (tileCollider != null)
        {
            // 1. 위에 있는 오브젝트들 물리 켜기 (HalfExtents 공식 적용)
            AwakePhysicsOnTile();

            // 2. NavMeshObstacle(Carving)을 켜서 이 타일 위치의 NavMesh에 구멍을 뚫는다.
            //    NavMesh는 게임 시작 시 한 번만 베이크되므로, 타일이 사라져도 NavMesh 표면은 그대로 남아
            //    AI가 빈 공간을 걸어다니는 원인이 된다. Carving은 런타임에 NavMesh를 동적으로 잘라준다.
            CarveNavMesh(tileCollider.bounds.size);

            // 2-1. 발판이 실제로 사라지는 시점이므로 이 칸을 '허공'으로 표시한다.
            //      AI가 잔존 NavMesh 위에 떠 있게 되는 경우를 감지/복구하는 데 쓰인다.
            if (GridX >= 0 && GridZ >= 0 && TileCollapseManager.Instance != null)
                TileCollapseManager.Instance.MarkCellCollapsed(GridX, GridZ);

            // 3. 낙하하는 타일 콜라이더 비활성화 (플레이어 밀림/끼임 버그 방지)
            tileCollider.enabled = false;
        }

        // 낙하 단계: 전체 transform을 Y축으로 가속 하강
        elapsed = 0f;
        while (elapsed < fallDuration)
        {
            float t = elapsed / fallDuration;
            float fallY = fallDistance * (t * t);
            transform.localPosition = originalPos + Vector3.down * fallY;

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPos + Vector3.down * fallDistance;
        gameObject.SetActive(false);
    }

    /// <summary>이 타일이 훑을 공간. 콜라이더의 월드 AABB에서 위아래로 넓힌 상자.</summary>
    private void GetOverlapBox(out Vector3 center, out Vector3 halfExtents)
    {
        Bounds tileBounds = tileCollider.bounds;

        float bottomY = tileBounds.min.y - overlapBoxDepth;
        float topY = tileBounds.max.y + overlapBoxHeight;

        // bounds는 월드 축에 정렬된 상자라 extents를 그대로 쓰려면 회전도 항등이어야 한다.
        halfExtents = new Vector3(tileBounds.extents.x, (topY - bottomY) * 0.5f, tileBounds.extents.z);
        center = new Vector3(tileBounds.center.x, (bottomY + topY) * 0.5f, tileBounds.center.z);
    }

    private void AwakePhysicsOnTile()
    {
        GetOverlapBox(out Vector3 boxCenter, out Vector3 halfExtents);

        // 배경 오브젝트 + AI (Player/Edible 레이어) 모두 감지.
        // [X4/G8] 여기는 붕괴 코루틴(FallRoutine) 한복판이라 NRE가 나면 이 타일만
        // 조용히 좀비(흔들리다 멈춘 채 영구 잔존, carve/IsOverVoid 미반영)가 된다 —
        //없는 레이어(-1)를 그대로 시프트하면 1<<31이 되어 엉뚱한 레이어가 섞인다.
        //그 방어는 GameLayers.MaskOf 안에 들어 있다
        //레이어 마스크의 출처는 GameLayers 하나다. 예전엔 DataManager가 같은 값을
        //Awake에서 복사해 들고 있었는데, 거쳐 갈 이유가 없는 통과 지점이었다
        int mask = GameLayers.BackGroundObjectMask;
        mask |= GameLayers.PlayerMask;
        mask |= GameLayers.EdibleMask;

        Collider[] OverlappedCols = Physics.OverlapBox(boxCenter, halfExtents, Quaternion.identity, mask);

        // 디버그: 시각화용 기록 + Scene 뷰에 라인으로 5초간 그림
        lastOverlapTime = Time.time;
        if (drawOverlapGizmo)
        {
            DebugDrawOverlapBox(boxCenter, halfExtents, Quaternion.identity, Color.red, 5f);
#if UNITY_EDITOR
            // 로그는 에디터에서만. 빌드에선 컴파일 자체가 빠져 프레임 스파이크/오버헤드 없음. (G3)
            Debug.Log($"[FallingTile] {name} AwakePhysicsOnTile: {OverlappedCols.Length}개 콜라이더 감지");
            foreach (var c in OverlappedCols)
            {
                if (c == null || c == tileCollider)
                    continue;
                Rigidbody r = c.GetComponentInParent<Rigidbody>();
                Debug.Log($"  └ {c.name} layer={LayerMask.LayerToName(c.gameObject.layer)} tag={c.tag} rb={(r != null ? r.name + "(kin=" + r.isKinematic + ")" : "없음")}");
            }
#endif
        }

        foreach (var col in OverlappedCols)
        {
            if (col == tileCollider)
                continue;

            // ★ 건드릴 자격을 '무엇을 바꾸기 전에' 본다
            //
            //   예전엔 AI를 끄고 CharacterController를 끄고 Rigidbody까지 붙인 뒤에야
            //   아래에서 소유권을 봤다. 그래서 클라가 호스트 소유인 남의 봇에
            //   물리를 붙여놓고 continue로 빠져나갔고, 그 뒤로 NetTransform이 보내주는
            //   위치와 클라 쪽 중력이 서로를 밀어 봇이 화면마다 다르게 보였다.
            //   (사람 플레이어를 걸러내는 이유는 아래 주석과 같다)
            if (col.GetComponentInParent<LanPlayerState>() != null)
                continue;

            if (IsDrivenElsewhere(col))
                continue;

            // Milk는 스크립트가 부모, 콜라이더가 자식이라 이 한 줄이 알아서 부모를 찾아간다.
            // 예전엔 여기에 Milk 특례가 하나 더 있었는데 transform.root — 즉 <b>타일</b>의
            // 루트에서 Rigidbody를 찾고 있었다. 격자 루트엔 Rigidbody가 없으니 rb가 null이 되어
            // 밀크가 통째로 걸러지는 코드였다. 특례 없이도 결과가 같으므로 지웠다.
            Rigidbody rb = col.GetComponentInParent<Rigidbody>();

            if (rb == null)
            {
                AIPlayerMovement aiBot = col.GetComponentInParent<AIPlayerMovement>();
                if (aiBot != null && !aiBot.IsOutOfPlay)
                {
                    DisableAIOnObject(aiBot.gameObject);

                    CharacterController cc = aiBot.GetComponent<CharacterController>();
                    if (cc != null)
                        cc.enabled = false;

                    rb = aiBot.gameObject.AddComponent<Rigidbody>();
                    MakeCollidersDynamicSafe(rb);
                    rb.useGravity = true;
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                }
                else
                    continue;
            }

            // ★ 사람 플레이어는 발판이 건드리지 않는다.
            //
            //   걸러내지 않으면 발판이 CharacterController를 꺼버리고 Rigidbody를 붙이는데,
            //   PlayerMovement는 계속 돌기 때문에
            //   "CharacterController.Move called on inactive controller"가 쏟아진다.
            //   (사람의 낙하는 PlayerMovement/초콜릿 경로가 따로 처리한다)
            if (rb.GetComponentInParent<LanPlayerState>() != null)
                continue;

            // 원격 오브젝트는 소유자 쪽에서만 물리를 돌린다
            if (IsDrivenElsewhere(rb))
                continue;

            DisableAIOnObject(rb.gameObject);

            CharacterController ccOnRb = rb.GetComponent<CharacterController>();
            if (ccOnRb != null)
                ccOnRb.enabled = false;

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
    /// 이 물체의 움직임을 다른 기계가 책임지고 있는가.
    ///
    /// ★ 씬에 손으로 놓은 것은 여기서 걸러내면 안 된다 (클라 화면에서만 젤리가 떠 있던 원인)
    ///   흡수 모드의 링 붕괴는 호스트 전용이 아니다. 모든 기계가 SyncedElapsed를 보고
    ///   똑같이 타일을 무너뜨리므로 이 함수도 클라에서 돈다.
    ///
    ///   그런데 씬 젤리는 NetIdentity를 갖고 OwnerId가 0이라 클라에선 IsSimulatedHere가
    ///   false다. 그래서 클라는 젤리 물리를 절대 켜지 않았고, 젤리 프리팹엔 위치 복제도
    ///   없어서 호스트가 떨어뜨린 결과도 오지 않았다 → 클라 화면에만 젤리가 공중에 남았다.
    ///   (NetIdentity가 없는 사탕·밀크는 이 필터를 안 타서 멀쩡히 떨어졌다. 그래서
    ///    "젤리만" 뜨는 것처럼 보였다.)
    ///
    ///   씬 오브젝트(NetId >= SCENE_ID_BASE)는 위치를 주고받지 않으니 소유권을 물을 이유가
    ///   없다. 각자 자기 화면에서 떨어뜨리면 되고, 흡수 판정은 어차피 호스트 권위다.
    /// </summary>
    private static bool IsDrivenElsewhere(Component c)
    {
        NetIdentity id = c.GetComponentInParent<NetIdentity>();
        if (id == null)
            return false;

        if (id.NetId >= NetConfig.SCENE_ID_BASE)
            return false;

        return !id.IsSimulatedHere;
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
            if (mcs[i] == null || mcs[i].convex)
                continue;
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

        navObstacle = carveObj.AddComponent<NavMeshObstacle>();
        navObstacle.shape = NavMeshObstacleShape.Box;
        navObstacle.size = new Vector3(colliderSize.x, colliderSize.y + 1f, colliderSize.z);
        navObstacle.center = Vector3.up * (colliderSize.y * 0.5f);
        navObstacle.carving = true;
        // 고정 오브젝트라 한 번만 carving되고 이후 재계산 비용이 없다.
        // false로 두면 stationary 타이머(~0.5초)를 기다리지 않고 즉시 구멍을 뚫어,
        // 그 사이 AI가 허공으로 튕기는 타이밍 공백을 없앤다.
        navObstacle.carveOnlyStationary = false;

        // 한 판 동안 무한 누적되는 carve 오브젝트를 매니저가 소유해 라운드 종료 시 일괄 정리한다. (G7)
        if (TileCollapseManager.Instance != null)
            TileCollapseManager.Instance.RegisterCarveObject(carveObj);
    }

    // AI 스크립트를 먼저 끄지 않으면 다음 프레임에 스스로 agent를 다시 켠다
    // (WanderingAI가 구동 권한을 잡으면 agent를 켠다) → 발판이 사라진 자리를 계속 걸어다닌다.
    // agent는 자식에 달린 프리팹도 있어 GetComponentsInChildren으로 훑는다.
    private static void DisableAIOnObject(GameObject obj)
    {
        foreach (var wandering in obj.GetComponentsInChildren<WanderingAI>(true))
            wandering.enabled = false;

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
        if (!drawOverlapGizmo)
            return;

        if (tileCollider == null)
            tileCollider = GetComponent<Collider>();
        if (tileCollider == null)
            return;

        GetOverlapBox(out Vector3 center, out Vector3 halfExtents);

        // 최근에 트리거된 박스는 빨강, 아니면 녹색
        bool recentlyTriggered = Time.time - lastOverlapTime < 5f;
        Color baseColor = recentlyTriggered ? Color.red : Color.green;

        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one);

        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);

        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.1f);
        Gizmos.DrawCube(Vector3.zero, halfExtents * 2f);

        Gizmos.matrix = prev;
    }
}