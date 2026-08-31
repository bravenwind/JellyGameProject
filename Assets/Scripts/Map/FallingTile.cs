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

    /// <summary>
    /// 이 점이 이 타일의 XZ 범위 안에 있는가. 높이는 보지 않는다 —
    /// "이 칸 위에 서 있나"만 묻는 것이고, 높이는 이미 OverlapBox가 걸렀다.
    /// </summary>
    private bool IsAboveThisTile(Vector3 worldPos)
    {
        Bounds b = tileCollider.bounds;
        return worldPos.x >= b.min.x && worldPos.x <= b.max.x
            && worldPos.z >= b.min.z && worldPos.z <= b.max.z;
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
            DebugDrawOverlapBox(boxCenter, halfExtents, Color.red, 5f);
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

            // ★ 자격 검사는 '무엇을 바꾸기 전에' 한 번만 한다
            //
            //   예전엔 AI를 끄고 CharacterController를 끄고 Rigidbody까지 붙인 뒤에야
            //   아래에서 소유권을 봤다. 그래서 클라가 호스트 소유인 남의 봇에
            //   물리를 붙여놓고 continue로 빠져나갔고, 그 뒤로 NetTransform이 보내주는
            //   위치와 클라 쪽 중력이 서로를 밀어 봇이 화면마다 다르게 보였다.
            //
            //   그걸 고치면서 같은 검사를 여기 위로 올렸는데, 아래에 있던 원본을 지우지 않아
            //   한동안 같은 질문을 두 번 하고 있었다. 아래 것은 절대 다른 답을 낼 수 없다 —
            //   rb는 col 자신이거나 col의 조상이므로, rb가 훑는 부모 사슬은 col이 훑는
            //   사슬의 뒷부분이다. rb가 찾아낼 NetIdentity·LanPlayerState는 col도 반드시 찾는다.
            //   (둘 사이에 또 다른 NetIdentity가 끼어 있으면 갈릴 수 있지만, 그런 중첩은
            //    프리팹 어디에도 없다)
            //
            //   사람 플레이어를 여기서 걸러내는 이유는 따로 있다: 발판이 사람의
            //   CharacterController를 꺼버리면 PlayerMovement는 계속 돌기 때문에
            //   "CharacterController.Move called on inactive controller"가 쏟아진다.
            //   사람의 낙하는 PlayerMovement/초콜릿 경로가 따로 처리한다.
            if (col.GetComponentInParent<LanPlayerState>() != null)
                continue;

            if (NetEntity.IsDrivenElsewhere(col))
                continue;

            // ★ 캐릭터는 '이 타일 위에 선' 것만 깨운다 (봇이 전원 꺼진 듯 굳던 원인)
            //
            //   OverlapBox는 콜라이더가 <b>조금이라도 겹치면</b> 잡는다. 그런데 봇은
            //   커지면서 캡슐 반지름이 0.5 × 스케일까지 자란다 — 4배면 2m다.
            //   타일이 14m니까, 큰 봇이 경계에서 2m 안쪽에 서 있기만 해도
            //   <b>옆 칸의 상자에 함께 걸린다.</b>
            //
            //   그러면 멀쩡한 발판 위에 서 있는데 옆 칸이 무너졌다는 이유로 물리로 넘어가
            //   PhysicsFall이 NavMeshAgent를 끄고, 봇은 그 자리에 굳는다.
            //   그 뒤엔 스스로 못 움직이니 제자리 마모로 자기 발판까지 무너져 탈락한다.
            //
            //   소품은 이 검사를 하지 않는다 — 작고, 옆 칸에 걸쳐 있으면 같이 떨어지는 게 맞다.
            //기준은 콜라이더가 아니라 봇의 루트다 — 메시 콜라이더는 자식이라 자리가 다르다.
            AIPlayerMovement standingBot = col.GetComponentInParent<AIPlayerMovement>();

            if (standingBot != null && !IsAboveThisTile(standingBot.transform.position))
                continue;

            // Milk는 스크립트가 부모, 콜라이더가 자식이라 이 한 줄이 알아서 부모를 찾아간다.
            // 예전엔 여기에 Milk 특례가 하나 더 있었는데 transform.root — 즉 <b>타일</b>의
            // 루트에서 Rigidbody를 찾고 있었다. 격자 루트엔 Rigidbody가 없으니 rb가 null이 되어
            // 밀크가 통째로 걸러지는 코드였다. 특례 없이도 결과가 같으므로 지웠다.
            Rigidbody rb = col.GetComponentInParent<Rigidbody>();

            // Rigidbody가 없는 건 NavMeshAgent가 직접 모는 봇뿐이다.
            // 붙여만 주면 나머지는 아래 PhysicsFall.Begin이 전부 한다.
            if (rb == null)
            {
                if (standingBot == null || standingBot.IsOutOfPlay)
                    continue;

                rb = standingBot.gameObject.AddComponent<Rigidbody>();
            }

            // 오목한 MeshCollider는 dynamic Rigidbody에 못 붙는다. 물리를 켜기 전에 정리한다.
            MakeCollidersDynamicSafe(rb);

            // ★ 여기서 손으로 물리를 켜지 않는다
            //   예전엔 AI 끄기·CharacterController 끄기·isKinematic·useGravity·
            //   collisionDetectionMode·WakeUp을 이 자리에 다 적어놨었다. 그런데 그건
            //   PhysicsFall.Begin이 하는 일과 <b>글자까지 같았다.</b> 사람·봇의 탈락은
            //   PhysicsFall을 쓰고 발판만 자기 사본을 쓰고 있었던 셈이다.
            bool wasKinematic = rb.isKinematic;

            PhysicsFall.Begin(rb.gameObject);

            // 가만히 놓여 있던 소품은 그냥 떨어지면 뻣뻣하다. 살짝 돌려 준다.
            // (원래 움직이고 있던 것은 이미 회전이 있으므로 건드리지 않는다)
            if (wasKinematic)
                rb.AddTorque(new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)), ForceMode.Impulse);
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

    // ─────────────────────────────────────────────────────────
    // 디버그 시각화
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// OverlapBox 영역을 Debug.DrawLine으로 표시 (Scene 뷰, duration초 동안).
    ///
    /// 회전 인자는 없다. 이 상자의 출처는 콜라이더 bounds라 항상 월드 축에 정렬돼 있고,
    /// 예전에 넘기던 값은 Quaternion.identity 하나뿐이었다.
    /// </summary>
    private static void DebugDrawOverlapBox(Vector3 center, Vector3 halfExtents, Color color, float duration)
    {
        // i의 비트 3개를 축 3개의 부호로 읽는다. 0이면 -, 1이면 +.
        //   비트0 → x, 비트1 → y, 비트2 → z
        // 0~7을 세는 것만으로 ±의 모든 조합 2³ = 8가지, 즉 꼭짓점 8개가 한 번씩 나온다.
        Vector3[] corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = center + new Vector3(
                (i & 1) == 0 ? -halfExtents.x : halfExtents.x,
                (i & 2) == 0 ? -halfExtents.y : halfExtents.y,
                (i & 4) == 0 ? -halfExtents.z : halfExtents.z);
        }
        // 모서리 = 축 하나만 따라 이동 = 비트 하나만 뒤집기.
        // 그래서 "비트가 정확히 하나만 다른 쌍"이 곧 모서리다. 2개 다르면 면의 대각선,
        // 3개 다르면 상자를 관통하는 대각선이라 그리면 안 된다.
        // 개수는 꼭짓점 8개 × 이웃 3개 ÷ 2(양 끝에서 두 번 셈) = 12개.
        //   1행 = x비트만 다름(가로), 2행 = y비트(세로), 3행 = z비트(깊이)
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

        // ★ Gizmos.matrix를 갈아끼우지 않는다
        //   Gizmos는 회전을 인자로 못 받아서, 돌아간 상자를 그리려면 좌표계 자체를
        //   바꿔치기하고 원점에 그리는 수밖에 없다. 예전엔 transform.rotation을 넣느라
        //   그렇게 했다. 지금 이 상자의 출처는 콜라이더 bounds — 이미 월드 축에
        //   정렬돼 있어 회전이 없다. 회전이 항등이면 행렬이 하는 일은 평행이동뿐이고,
        //   그건 DrawWireCube의 첫 인자가 이미 한다.
        //
        //   Gizmos.matrix는 정적 전역이라 되돌리는 코드까지 딸려온다. 없앨 수 있으면 없앤다.

        // DrawWireCube는 halfExtents가 아니라 <b>전체 크기</b>를 받는다.
        // OverlapBox와 단위가 달라서 * 2를 빠뜨리면 실제 검사 범위의 절반만 그려진다.
        Vector3 size = halfExtents * 2f;

        // 선만 그리면 이웃 타일들의 선과 엉켜 어느 상자 건지 구분이 안 된다.
        // 옅게 속을 채워 부피로 보이게 하면 소품이 안에 잠겼는지 밖인지가 눈에 들어온다.
        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f);
        Gizmos.DrawWireCube(center, size);

        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.1f);
        Gizmos.DrawCube(center, size);
    }
}