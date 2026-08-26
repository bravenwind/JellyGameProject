using System.Collections.Generic;
using UnityEngine;
using JellyNet;

public class TileCollapseManager : MonoBehaviour
{
    public static TileCollapseManager Instance { get; private set; }

    [Header("Grid")]
    [Tooltip("타일들의 부모 Transform (비워두면 이 오브젝트에서 탐색)")]
    [SerializeField] private Transform gridParent;

    [Header("붕괴 타이밍")]
    [Tooltip("게임 시작 후 붕괴 시작까지의 시간 (초)")]
    [SerializeField] private float collapseStartTime = 90f;

    [Tooltip("각 링 붕괴 간격 (초). autoRingInterval이 켜져 있으면 자동으로 덮어쓴다.")]
    [SerializeField] private float ringInterval = 15f;

    [Header("붕괴 주기 자동 계산")]
    [Tooltip("마지막 링이 꺼지는 시점이 '게임 종료 N초 전'이 되도록 간격을 역산한다.")]
    [SerializeField] private bool autoRingInterval = true;

    [Tooltip("마지막 링이 다 꺼진 뒤 남길 시간 (초).")]
    [SerializeField] private float endMargin = 5f;

    [Tooltip("서 있을 때보다 이만큼 이상 떠 있으면 점프로 본다(마모 안 됨). 점프 높이보다 작아야 한다.")]
    [SerializeField] private float groundCheckDistance = 0.6f;

    [Tooltip("캐릭터가 커질 때 접지 기준이 따라 올라가는 속도(초당). 0이면 안 따라간다.")]
    [SerializeField] private float standGapDrift = 0.15f;

    [Tooltip("같은 링 내 타일 간 연쇄 딜레이 (초) — 0이면 동시에 떨어짐")]
    [SerializeField] private float tileDelay = 0f;

    [Header("타일 애니메이션")]
    [Tooltip("경고 흔들림 시간 (초)")]
    [SerializeField] private float warningDuration = 3f;

    [Tooltip("떨어지는 시간 (초)")]
    [SerializeField] private float fallDuration = 2f;

    [Tooltip("떨어지는 거리")]
    [SerializeField] private float fallDistance = 30f;

    private GameObject[,] tiles;
    private int width, height;
    private int maxRing;
    private int lastCollapsedRing = -1;
    private int lastShakenRing = -1;
    private Vector3 gridOrigin;
    private float stepX, stepZ;

    // 실제로 붕괴(carve)가 끝나 발판이 사라진 칸들. IsOverVoid 판정용.
    private HashSet<int> collapsedCells = new HashSet<int>();

    private Dictionary<int, int> tileStepCounts = new Dictionary<int, int>();
    private Dictionary<int, int> entityCurrentTile = new Dictionary<int, int>();
    private Dictionary<int, float> entityDwellTime = new Dictionary<int, float>();
    private Dictionary<int, Color> tileOriginalColors = new Dictionary<int, Color>();

    // 경고 색 변경용 셰이더 프로퍼티(URP=_BaseColor / 빌트인=_Color). MaterialPropertyBlock으로
    // 칠하면 .material처럼 인스턴스를 복제하지 않아 배칭이 유지된다. (K1 — FallingTile G3와 동일 패턴)
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock mpb;

    private float stepProcessTimer;
    private const float STEP_PROCESS_INTERVAL = 0.15f;

    // FallingTile이 붕괴 시 루트에 생성하는 NavCarve_* 장애물들. 구멍을 유지하기 위해 한 판 동안은
    // 살려 두지만, 매니저가 소유 목록으로 들고 있다가 라운드/씬 종료 시 일괄 정리한다. (G7)
    private readonly List<GameObject> carveObjects = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else { Destroy(this); return; }

        if (gridParent == null)
            gridParent = transform;
    }

    /// <summary>FallingTile이 만든 carve 장애물을 매니저 소유로 등록한다. (G7)</summary>
    public void RegisterCarveObject(GameObject carveObj)
    {
        if (carveObj != null)
            carveObjects.Add(carveObj);
    }

    /// <summary>등록된 carve 장애물을 모두 파괴하고 목록을 비운다(라운드 종료/씬 정리용). (G7)</summary>
    public void ClearCarveObjects()
    {
        for (int i = 0; i < carveObjects.Count; i++)
            if (carveObjects[i] != null)
                Destroy(carveObjects[i]);
        carveObjects.Clear();
    }

    private void OnDestroy()
    {
        // 씬이 carve 오브젝트를 자동 정리하긴 하지만, 매니저가 소유한 만큼 명시적으로 정리해
        // 누수/잔존(추가 씬 구성 등)을 차단한다. (G7)
        if (Instance == this)
            Instance = null;
        ClearCarveObjects();
    }

    private void Start()
    {
        CollectTiles();
        if (width == 0 || height == 0)
            return;
        maxRing = Mathf.Min(width, height) / 2;
    }

    /// <summary>
    /// 링 붕괴의 기준 시각.
    ///
    /// ★ -1을 반환하면 Update()가 즉시 return해서 링이 한 번도 안 무너진다.
    ///   에러가 안 나므로 증상만 보고는 원인을 찾기 어렵다.
    /// </summary>
    private float GetSyncedElapsed()
    {
        var flow = LanGameFlow.Instance;
        return flow != null ? flow.Elapsed : -1f;
    }

    /// <summary>게임이 실제로 진행 중인가. LanGameFlow의 단계를 본다.</summary>
    // ═════════════════════════════════════════════════════════
    //  붕괴 주기 역산
    // ═════════════════════════════════════════════════════════
    //
    // ★ 무엇을 맞추는가
    //   "마지막 링이 꺼질 때 게임 시간이 endMargin(5초) 남는다."
    //
    //   링은 collapseStartTime부터 ringInterval 간격으로 하나씩 무너지고,
    //   마지막 링의 인덱스는 maxRing - 1이다. 즉 마지막 링이 무너지기 시작하는 때는
    //
    //       collapseStartTime + (maxRing - 1) * ringInterval
    //
    //   여기에 경고 흔들림(warningDuration)과 낙하(fallDuration)가 더해져야
    //   '다 꺼진' 시점이 된다. 그 시점이 gameDuration - endMargin이어야 하므로
    //
    //       ringInterval = (gameDuration - endMargin - warning - fall - collapseStartTime)
    //                      / (maxRing - 1)
    //
    //   맵 크기(maxRing)와 게임 시간이 바뀌어도 인스펙터를 다시 만질 필요가 없다.
    private bool intervalComputed;

    private void ComputeRingInterval()
    {
        if (!autoRingInterval || intervalComputed)
            return;
        if (maxRing <= 1)
            return;

        var flow = LanGameFlow.Instance;
        float duration = flow != null ? flow.GameDuration : -1f;
        if (duration <= 0f)
            return;      // 아직 모른다 — 다음 프레임에 다시 시도

        float usable = duration - endMargin - warningDuration - fallDuration - collapseStartTime;

        if (usable <= 0f)
        {
            Debug.LogWarning("[타일] 붕괴 시작(" + collapseStartTime + "s)이 게임 시간("
                             + duration + "s)에 비해 너무 늦어 링을 다 못 꺼뜨립니다. "
                             + "collapseStartTime을 줄여주세요. 자동 계산을 끕니다.");
            intervalComputed = true;
            return;
        }

        ringInterval = usable / (maxRing - 1);
        intervalComputed = true;

        Debug.Log("[타일] 링 " + maxRing + "개 · 게임 " + duration + "s → 간격 "
                  + ringInterval.ToString("F2") + "s "
                  + "(마지막 링 완료 " + (duration - endMargin).ToString("F1") + "s 지점)");
    }

    private bool IsRunning()
    {
        var flow = LanGameFlow.Instance;
        return flow != null && flow.Phase == GamePhase.Playing;
    }

    private void CollectTiles()
    {
        var gen = gridParent.GetComponent<AutoGridMapGenerator>();
        if (gen != null)
        {
            width = gen.width;
            height = gen.height;
        }
        else
        {
            int maxX = 0, maxZ = 0;
            foreach (Transform child in gridParent)
            {
                if (TryParseTileName(child.name, out int x, out int z))
                {
                    if (x > maxX)
                        maxX = x;
                    if (z > maxZ)
                        maxZ = z;
                }
            }
            width = maxX + 1;
            height = maxZ + 1;
        }

        tiles = new GameObject[width, height];
        foreach (Transform child in gridParent)
        {
            if (TryParseTileName(child.name, out int x, out int z)
                && x < width && z < height)
            {
                tiles[x, z] = child.gameObject;
            }
        }

        // 월드 → 타일 좌표 변환용 원점/간격 캐시
        if (width > 0 && height > 0 && tiles[0, 0] != null)
            gridOrigin = tiles[0, 0].transform.position;
        if (width > 1 && tiles[1, 0] != null && tiles[0, 0] != null)
            stepX = tiles[1, 0].transform.position.x - tiles[0, 0].transform.position.x;
        if (height > 1 && tiles[0, 1] != null && tiles[0, 0] != null)
            stepZ = tiles[0, 1].transform.position.z - tiles[0, 0].transform.position.z;
    }

    private bool TryParseTileName(string name, out int x, out int z)
    {
        x = z = 0;
        string[] parts = name.Split('_');
        return parts.Length >= 3
            && parts[0] == "Tile"
            && int.TryParse(parts[1], out x)
            && int.TryParse(parts[2], out z);
    }

    private int GetRing(int x, int z)
    {
        return Mathf.Min(x, z, width - 1 - x, height - 1 - z);
    }

    // 첫 패스는 마모 없이 현재 위치만 기록한다.
    //
    // 판이 시작될 때 entityCurrentTile/entityDwellTime(체류 마모 상태)이 비어 있어서,
    // 그대로 두면 첫 UpdateStepCollapse가 맵 위 전 개체를 "이전 칸 없음(-1)→현재 칸" 전이로
    // 오인해 모두의 현재 타일을 동시에 한 번씩 마모시킨다. 임계 직전 타일들이 한꺼번에
    // 무너지는 셈이다. 첫 패스를 건너뛰면 그 다음부터 정상적인 '이동 감지' 마모가 이뤄진다.
    private bool needsStepGrace = true;

    private void Update()
    {
        if (!IsRunning())
            return;

        ComputeRingInterval();   // 게임 시간을 알게 된 뒤 한 번만 계산된다

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            UpdateStepCollapse();
            return;
        }

        float elapsed = GetSyncedElapsed();
        if (elapsed < 0f)
            return;

        int nextShakeRing = lastCollapsedRing + 1;
        if (nextShakeRing < maxRing && nextShakeRing > lastShakenRing)
        {
            float shakeStartTime = collapseStartTime + (nextShakeRing - 1) * ringInterval;
            if (elapsed >= shakeStartTime)
            {
                lastShakenRing = nextShakeRing;
                StartIdleShakeOnRing(nextShakeRing);
            }
        }

        if (elapsed < collapseStartTime)
            return;

        int targetRing = Mathf.FloorToInt((elapsed - collapseStartTime) / ringInterval);
        targetRing = Mathf.Min(targetRing, maxRing - 1);

        while (lastCollapsedRing < targetRing)
        {
            lastCollapsedRing++;
            CollapseRingAnimated(lastCollapsedRing);
        }
    }

    private void UpdateStepCollapse()
    {
        // [LAN 이식] 밟아서 마모시키는 판정은 호스트만 한다(권위 단일화).
        if (NetManager.Instance != null
            && !NetManager.Offline
            && !NetManager.Instance.IsHost) return;

        if (stepX == 0f || stepZ == 0f)
            return;

        // 첫 패스: 현재 위치만 기록하고 마모는 건너뛴다.
        if (needsStepGrace)
        {
            SeedEntityTilesNoWear();
            needsStepGrace = false;
            stepProcessTimer = 0f;
            return;
        }

        stepProcessTimer += Time.deltaTime;
        if (stepProcessTimer < STEP_PROCESS_INTERVAL)
            return;
        float dt = stepProcessTimer;
        stepProcessTimer = 0f;

        //★ 봇 루프에 IsBeingAbsorbed가 빠져 있었다
        //  사람은 IsOutOfPlay(탈락 OR 흡수당하는 중) 하나로 걸렀는데
        //  봇은 IsEliminated만 봤다. 그래서 흡수 연출이 도는 0.8초 동안
        //  이미 상대에게 빨려 들어가는 봇이 지나온 발판을 계속 마모시켰다.
        //  목록이 하나면 조건도 하나라 이런 게 생길 자리가 없다.
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null || e.IsOutOfPlay)
                continue; // 탈락/흡수 판정 단일 출처 (G6/K2)
            TryStepAt(e.Transform.position, e.EntityId, dt);
        }
    }

    /// <summary>
    /// 지금 실제로 그 발판을 <b>밟고 있는가</b> — 타일 윗면과의 높이 차이로 본다.
    ///
    /// ★ 왜 CharacterController를 안 쓰는가 (한 번 이걸로 크게 물렸다)
    ///   isGrounded가 가장 정확해 보이지만, <b>원격 플레이어는 CharacterController가 꺼져 있다.</b>
    ///   LanPlayerSetup이 원격 사본의 물리를 꺼서 위치 동기화와 싸우지 않게 하기 때문이다.
    ///   그래서 그걸 기준으로 삼으면 호스트 화면에서 <b>클라이언트가 밟은 발판이 영영 안 닳는다.</b>
    ///   NavMeshAgent도 마찬가지로 원격 봇에서는 꺼져 있다.
    ///
    ///   반면 '위치'는 누구에게나 동기화돼 있다. 타일 윗면에서 얼마나 떠 있는지만 보면
    ///   로컬·원격, 사람·봇을 가리지 않고 같은 기준으로 판단할 수 있다.
    ///
    /// ★ 점프는 이걸로 걸러진다
    ///   점프하면 그 순간 타일 위로 확실히 떠오른다. 허용치(groundCheckDistance)보다
    ///   높이 뜨면 밟은 것으로 세지 않는다.
    /// </summary>
    // 개체별로 '서 있을 때의 높이 차이'를 관측해 기억한다.
    private readonly Dictionary<int, float> standGap = new Dictionary<int, float>();

    private bool IsOnTile(int x, int z, float entityY, int entityId, float dt)
    {
        GameObject tile = tiles[x, z];
        if (tile == null)
            return false;

        // ★ 절대 높이로 판단하면 안 된다 (한 번 이걸로 발판이 통째로 안 닳았다)
        //
        //   tile.transform.position.y 는 <b>윗면이 아니라 피벗</b>이고,
        //   캐릭터의 position.y 도 발밑이 아니라 몸 중앙이다. 게다가 젤리는 크기가
        //   자라면서 중앙이 위로 올라간다. 그래서 "0.8 이내" 같은 고정값을 쓰면
        //   맵·캐릭터·크기에 따라 맞았다 틀렸다 한다.
        //
        //   대신 <b>서 있을 때의 간격을 관측으로 배운다.</b>
        //   가만히 서 있는 순간의 간격이 곧 기준이고, 점프하면 그보다 확실히 커진다.
        //   피벗이 어디에 있든, 캐릭터가 얼마나 크든 알 필요가 없다.
        float gap = entityY - tile.transform.position.y;

        float known;
        if (!standGap.TryGetValue(entityId, out known))
            known = gap;

        // 더 낮은 값이 보이면 그게 진짜 '붙어 있는' 간격이다 → 즉시 내린다.
        if (gap < known)
            known = gap;

        // 크기가 자라면 중앙이 올라가므로 기준도 천천히 따라 올라가야 한다.
        // (안 그러면 커진 뒤로 영원히 '공중에 떠 있음'으로 읽힌다)
        known += dt * standGapDrift;

        standGap[entityId] = known;

        return gap <= known + groundCheckDistance;
    }

    // 현재 각 개체가 서 있는 칸을 마모 없이 기록만 한다(첫 패스용).
    private void SeedEntityTilesNoWear()
    {
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null || e.IsOutOfPlay)
                continue;
            SeedTile(e.Transform.position, e.EntityId);
        }
    }

    private void SeedTile(Vector3 worldPos, int entityId)
    {
        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ);
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        entityCurrentTile[entityId] = x * 10000 + z;
        entityDwellTime[entityId] = 0f;
    }

    private void TryStepAt(Vector3 worldPos, int entityId, float dt)
    {
        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ);

        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        if (tiles[x, z] == null)
            return;

        // ★ 공중에 떠 있으면 밟은 게 아니다.
        //   점프해서 그 위를 지나가는 것만으로 발판이 닳으면 안 된다.
        //   체류 타이머도 함께 리셋해, 착지하는 순간부터 다시 세게 한다.
        if (!IsOnTile(x, z, worldPos.y, entityId, dt))
        {
            entityDwellTime[entityId] = 0f;
            return;
        }

        int tileKey = x * 10000 + z;

        // 새 타일로 이동: 마지막 타일에서 현재 타일까지의 경로상 칸들을 마모시키고 체류 타이머 초기화.
        // (현재 위치만 0.15초마다 샘플링하므로, 대쉬 등으로 빠르게 지나가면 중간 칸이 통째로 건너뛰어져
        //  '밟아도 안 떨어지는' 현상이 생긴다. 마지막→현재 칸을 라인으로 훑어 건너뛴 칸도 마모한다.)
        if (!entityCurrentTile.TryGetValue(entityId, out int lastTile) || lastTile != tileKey)
        {
            if (!entityCurrentTile.ContainsKey(entityId))
                lastTile = -1;
            WearTilePath(lastTile, x, z, tileKey);
            entityCurrentTile[entityId] = tileKey;
            entityDwellTime[entityId] = 0f;
            return;
        }

        // 같은 타일에 계속 머무름: 일정 시간 이상 머물면 추가로 마모(견디는 횟수 감소)
        var dm = DataManager.Instance;
        float idleWear = dm != null ? dm.StepTileIdleWearSeconds : 0f;
        if (idleWear <= 0f)
            return;

        entityDwellTime.TryGetValue(entityId, out float dwell);
        dwell += dt;
        if (dwell >= idleWear)
        {
            dwell -= idleWear; // 초과분 보존 → 이후에도 idleWear마다 계속 마모
            WearTile(x, z, tileKey);
        }
        entityDwellTime[entityId] = dwell;
    }

    /// <summary>마지막으로 밟힌 칸(lastTile)에서 현재 칸(x1,z1)까지 그리드 라인을 따라 각 칸을 마모시킨다.
    /// 빠른 이동(대쉬)으로 샘플 간 여러 칸을 건너뛰어도 지나간 칸이 모두 마모되도록 보강한다.
    /// 시작 칸(lastTile)은 이미 진입 때 마모됐으므로 제외하고, 비정상적으로 먼 이동(리스폰/Warp 추정)은
    /// 라인 마모를 생략해 텔레포트가 칸 한 줄을 통째로 깎지 않도록 한다.</summary>
    private void WearTilePath(int lastTile, int x1, int z1, int tileKey1)
    {
        if (lastTile < 0)
        {
            WearTile(x1, z1, tileKey1); // 최초 등록(이전 칸 없음) — 현재 칸만
            return;
        }

        int x0 = lastTile / 10000;
        int z0 = lastTile % 10000;
        int dxAbs = Mathf.Abs(x1 - x0);
        int dzAbs = Mathf.Abs(z1 - z0);

        // 인접(또는 같은) 칸이면 목적지 1칸만. 너무 멀면(리스폰/Warp 등 순간이동) 라인 생략, 목적지만.
        const int MAX_SWEEP = 8;
        if (dxAbs + dzAbs <= 1 || dxAbs + dzAbs > MAX_SWEEP)
        {
            WearTile(x1, z1, tileKey1);
            return;
        }

        int steps = Mathf.Max(dxAbs, dzAbs);
        for (int i = 1; i <= steps; i++) // i=0(시작 칸)은 이미 마모됨 → 제외
        {
            float t = (float)i / steps;
            int xi = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int zi = Mathf.RoundToInt(Mathf.Lerp(z0, z1, t));
            if (xi < 0 || xi >= width || zi < 0 || zi >= height)
                continue;
            if (tiles[xi, zi] == null)
                continue;
            WearTile(xi, zi, xi * 10000 + zi);
        }
    }

    /// <summary>타일 견디는 횟수를 1 소모시키고, 한계 도달 시 붕괴·아니면 색을 어둡게 방송한다.</summary>
    private void WearTile(int x, int z, int tileKey)
    {
        if (tiles[x, z] == null)
            return;

        tileStepCounts.TryGetValue(tileKey, out int count);
        count++;
        tileStepCounts[tileKey] = count;

        var dm = DataManager.Instance;
        int maxSteps = dm != null ? dm.StepTileStepsToCollapse : 3;

        if (NetWorld.Instance == null)
            return;

        if (count >= maxSteps)
            CollapseStepTile(x, z);          // 안에서 전파까지 한다
        else                   BroadcastDarken(x, z, count, maxSteps);
    }

    /// <summary>
    /// 어두워지는 단계를 전원에게 알린다.
    ///
    /// ★ 왜 붕괴와 같은 메시지를 쓰는가
    ///   붕괴는 TileCollapse 메시지가 이미 있다. 어두워지는 건 '몇 번 밟혔는지'만
    ///   추가로 필요한데, 그건 각 클라가 스스로 셀 수 없다(자기 발밑만 알기 때문).
    ///   그래서 호스트가 세어서 알려준다. 메시지 하나를 재활용하고 count를 실어 보낸다.
    /// </summary>
    private void BroadcastDarken(int x, int z, int count, int maxSteps)
    {
        DarkenStepTile(x, z, count, maxSteps);                       // 호스트 자기 화면
        NetWorld.Instance.BroadcastTileWear(x, z, count, maxSteps);
    }

    public void DarkenStepTile(int x, int z, int stepCount, int maxSteps)
    {
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        if (tiles[x, z] == null)
            return;

        int tileKey = x * 10000 + z;

        // 방송에 실려 온 마모 카운트를 클라도 자기 dict에 기록한다.
        // 판정은 호스트 단독이지만 근거 상태를 복제해 두면
        // 클라의 IsPositionDangerous(봇 AI가 위험 타일을 피하는 판단)도 실제 마모를 반영한다.
        tileStepCounts[tileKey] = stepCount;

        Renderer rend = tiles[x, z].GetComponentInChildren<Renderer>();
        if (rend == null || rend.sharedMaterial == null)
            return;

        // 색은 sharedMaterial로 읽고(인스턴스 복제 없음) MaterialPropertyBlock으로 칠한다 → 밟힌
        // Push 타일마다 머티리얼 사본이 생겨 배칭이 깨지던 문제를 막는다. (K1 — FallingTile G3와 동일)
        int colorPropId = rend.sharedMaterial.HasProperty(BaseColorId) ? BaseColorId : ColorId;
        if (!rend.sharedMaterial.HasProperty(colorPropId))
            return;

        if (!tileOriginalColors.TryGetValue(tileKey, out Color original))
        {
            original = rend.sharedMaterial.GetColor(colorPropId);
            tileOriginalColors[tileKey] = original;
        }

        float t = (float)stepCount / maxSteps;
        Color danger = new Color(original.r * 0.3f, original.g * 0.15f, original.b * 0.1f);

        if (mpb == null)
            mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetColor(colorPropId, Color.Lerp(original, danger, t));
        rend.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// 밟아서 마모된 타일 하나를 무너뜨린다.
    ///
    /// [LAN 이식] 판정은 호스트만 하므로, 그 결과를 전원에게 알려야 한다.
    ///   (링 단위 붕괴는 시간 기반이라 각자 같은 시각에 알아서 무너지지만,
    ///    이건 플레이어가 밟은 자리라 호스트만 알 수 있다)
    /// </summary>
    public void CollapseStepTile(int x, int z)
    {
        CollapseStepTile(x, z, true);
    }

    public void CollapseStepTile(int x, int z, bool broadcast)
    {
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        if (tiles[x, z] == null)
            return;

        if (broadcast && NetWorld.Instance != null)
            NetWorld.Instance.BroadcastTileCollapse(x, z);

        var dm = DataManager.Instance;
        float warn = dm != null ? dm.StepTileWarningDuration : 1.5f;
        float delay = dm != null ? dm.StepTileCollapseDelay : 2f;

        var ft = tiles[x, z].GetComponent<FallingTile>();
        if (ft == null)
            ft = tiles[x, z].AddComponent<FallingTile>();

        ft.SetGridPos(x, z);
        ft.StartFall(warn, fallDuration, fallDistance, Mathf.Max(0f, delay - warn));
        tiles[x, z] = null;
    }

    private void StartIdleShakeOnRing(int ring)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (GetRing(x, z) == ring && tiles[x, z] != null)
                {
                    var ft = tiles[x, z].GetComponent<FallingTile>();
                    if (ft == null)
                        ft = tiles[x, z].AddComponent<FallingTile>();
                    ft.StartIdleShake();
                }
            }
        }
    }

    private void CollapseRingAnimated(int ring)
    {
        float delay = 0f;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (GetRing(x, z) == ring && tiles[x, z] != null)
                {
                    var ft = tiles[x, z].GetComponent<FallingTile>();
                    if (ft == null)
                        ft = tiles[x, z].AddComponent<FallingTile>();
                    ft.SetGridPos(x, z);
                    ft.StartFall(warningDuration, fallDuration, fallDistance, delay);
                    tiles[x, z] = null;
                    if (tileDelay > 0f)
                        delay += tileDelay;
                }
            }
        }
    }

    /// <summary>
    /// 월드 좌표가 "위험한 타일" 위인지 판정.
    /// 위험 = 이미 무너졌거나 / 흔들리는 중이거나 / 곧 무너질 예정인 링에 속함.
    /// AI는 이 좌표를 목적지로 잡지 않아야 함.
    /// </summary>
    public bool IsPositionDangerous(Vector3 worldPos)
    {
        if (stepX == 0f || stepZ == 0f)
            return false;

        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ);

        if (x < 0 || x >= width || z < 0 || z >= height)
            return true;

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            if (tiles[x, z] == null)
                return true;
            int tileKey = x * 10000 + z;
            if (tileStepCounts.TryGetValue(tileKey, out int count))
            {
                var dm = DataManager.Instance;
                int maxSteps = dm != null ? dm.StepTileStepsToCollapse : 3;
                if (count >= maxSteps - 1)
                    return true;
            }
            return false;
        }

        int ring = GetRing(x, z);
        return ring <= lastShakenRing;
    }

    /// <summary>
    /// 타일이 실제로 붕괴(carve)된 시점에 FallingTile이 호출. 해당 칸을 '허공'으로 표시한다.
    /// 주의: tiles[x,z]=null은 붕괴 '예약' 시점에 설정되지만 타일은 경고 흔들림 동안
    ///       물리적으로 남아 있으므로, 진짜 사라진 시점(carve)을 따로 기록해야 오탐이 없다.
    /// </summary>
    public void MarkCellCollapsed(int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        collapsedCells.Add(x * 10000 + z);
    }

    /// <summary>
    /// 월드 좌표 아래에 실제 발판이 없는지(=허공) 판정.
    /// 그리드 밖이거나 이미 붕괴 완료(carve)된 칸이면 true.
    /// NavMesh가 잔존하거나 타일 밖으로 베이크돼 AI가 허공에 떠 있는 상황을 잡는 데 쓴다.
    /// </summary>
    public bool IsOverVoid(Vector3 worldPos)
    {
        if (stepX == 0f || stepZ == 0f)
            return false;

        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ);

        if (x < 0 || x >= width || z < 0 || z >= height)
            return true;
        return collapsedCells.Contains(x * 10000 + z);
    }

    /// <param name="avoidDangerous">true면 물리적으로 남아 있어도 곧 붕괴할(IsPositionDangerous)
    /// 타일은 후보에서 제외한다. Push 모드 도피처럼 '진짜 안전한' 칸이 필요할 때 사용.
    /// 허공 탈출처럼 일단 발판 있는 칸이면 되는 경우엔 false(기본).</param>
    /// <summary>
    /// 도망칠 발판을 고른다. <b>가까운 곳이 아니라 '살 수 있는 곳'</b>을 찾는다.
    ///
    /// ★ 왜 FindNearestSafeTile로는 부족한가
    ///   그건 거리만 본다. 그래서 위협(플레이어) 바로 옆의 안전한 타일이 뽑히기도 한다.
    ///   봇 입장에서는 무너지는 발판은 피했는데 <b>때리려는 사람 품으로 뛰어드는</b> 셈이다.
    ///   반대로 위협만 피하면 발판 없는 허공으로 달려간다.
    ///
    ///   두 가지를 같이 봐야 한다:
    ///     · 위협에서 멀어질수록 좋다
    ///     · 지금 자리에서 너무 멀면 도착 전에 잡힌다
    ///
    ///   그래서 후보 타일마다 점수를 매겨 가장 높은 곳을 고른다.
    /// </summary>
    /// <param name="threatPos">피하고 싶은 대상의 위치. 없으면 worldPos를 넣으면 거리만 본다.</param>
    public bool FindEscapeTile(Vector3 worldPos, Vector3 threatPos, out Vector3 safePos,
                               int searchTiles = 6)
    {
        safePos = Vector3.zero;
        if (stepX == 0f || stepZ == 0f)
            return false;

        int cx = Mathf.Clamp(Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX), 0, width - 1);
        int cz = Mathf.Clamp(Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ), 0, height - 1);

        float best = float.MinValue;
        bool found = false;

        for (int dx = -searchTiles; dx <= searchTiles; dx++)
        {
            for (int dz = -searchTiles; dz <= searchTiles; dz++)
            {
                int tx = cx + dx, tz = cz + dz;
                if (tx < 0 || tx >= width || tz < 0 || tz >= height)
                    continue;
                if (tiles[tx, tz] == null)
                    continue;

                Vector3 tilePos = tiles[tx, tz].transform.position;
                if (IsPositionDangerous(tilePos))
                    continue;      // 곧 무너질 곳은 후보가 아니다

                float fromThreat = Vector3.Distance(tilePos, threatPos);
                float fromMe = Vector3.Distance(tilePos, worldPos);

                // 위협에서 멀수록 +, 내게서 멀수록 −.
                // 위협 쪽 가중치를 크게 둬서 "조금 더 뛰더라도 반대편으로" 가게 한다.
                float score = fromThreat * 1.5f - fromMe;

                if (score > best)
                {
                    best = score;
                    safePos = tilePos;
                    found = true;
                }
            }
        }

        return found;
    }

    public bool FindNearestSafeTile(Vector3 worldPos, out Vector3 safePos, bool avoidDangerous = false)
    {
        safePos = Vector3.zero;
        if (stepX == 0f || stepZ == 0f)
            return false;

        int cx = Mathf.Clamp(Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX), 0, width - 1);
        int cz = Mathf.Clamp(Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ), 0, height - 1);

        int maxRadius = Mathf.Max(width, height);
        for (int r = 1; r <= maxRadius; r++)
        {
            float bestDist = float.MaxValue;
            bool found = false;

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r)
                        continue;

                    int tx = cx + dx;
                    int tz = cz + dz;
                    if (tx < 0 || tx >= width || tz < 0 || tz >= height)
                        continue;
                    if (tiles[tx, tz] == null)
                        continue;

                    Vector3 tilePos = gridOrigin + new Vector3(tx * stepX, 0f, tz * stepZ);

                    // 발판은 남아 있어도 마모가 한계 직전(곧 붕괴)인 타일은 '안전'이 아니다.
                    // 이 검사가 없으면 봇들이 닳은 타일로 우르르 도피→서로의 step 마모를 가속해
                    // 한곳에 모여 동시에 무너지는 현상이 생긴다(IsPositionDangerous와 기준 통일).
                    if (avoidDangerous && IsPositionDangerous(tilePos))
                        continue;

                    float dist = (tilePos - worldPos).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        safePos = tilePos;
                        found = true;
                    }
                }
            }
            if (found)
                return true;
        }
        return false;
    }

    /// <summary>
    /// NavMeshPath가 위험 구간을 지나는지 검사.
    /// NavMeshObstacle carving이 지연되는 동안의 이중 안전장치.
    ///
    /// ★ 예전엔 코너만 봤다
    ///   NavMesh의 코너는 '경로가 꺾이는 지점'이지 '한 칸 간격'이 아니다.
    ///   탁 트인 곳을 가로지르면 코너가 출발점과 도착점 둘뿐인 20m 직선이 나오는데,
    ///   그 사이에 무너진 칸이 있어도 검사에 걸리지 않았다.
    ///   젤리가 위험 경로를 안전하다고 판단해 그대로 걸어 들어가던 구멍이다.
    ///
    ///   이제 코너 사이를 <b>타일 크기 기준으로 등분</b>해 함께 본다.
    ///   간격을 타일 반 칸으로 잡은 이유는 그보다 성기면 한 칸을 통째로 건너뛸 수 있어서다.
    ///   구간이 길수록 표본이 자동으로 늘어나고, 한 구간당 상한을 둬 비용이 폭발하지 않게 한다.
    /// </summary>
    private const int MAX_SAMPLES_PER_SEGMENT = 16;

    public bool IsPathDangerous(Vector3[] corners, int count)
    {
        if (stepX == 0f || stepZ == 0f || count == 0)
            return false;

        //타일 반 칸. 이보다 성기면 표본 사이로 한 칸이 통째로 빠져나간다
        float sampleStep = Mathf.Min(stepX, stepZ) * 0.5f;

        if (IsPositionDangerous(corners[0]))
            return true;

        for (int i = 1; i < count; i++)
        {
            Vector3 from = corners[i - 1];
            Vector3 to = corners[i];

            //구간 길이에 비례해 등분 수가 늘어난다
            int steps = Mathf.Clamp(
                Mathf.CeilToInt(Vector3.Distance(from, to) / sampleStep),
                1, MAX_SAMPLES_PER_SEGMENT);

            //j = 0은 이전 코너라 이미 봤다. 1..steps 를 보면 끝점(코너 i)까지 포함된다
            for (int j = 1; j <= steps; j++)
            {
                if (IsPositionDangerous(Vector3.Lerp(from, to, (float)j / steps)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 현재 안전 영역의 월드 좌표 바운드를 반환.
    /// 젤리 스폰 범위 등에 사용.
    /// </summary>
    public bool GetSafeBounds(out Vector3 min, out Vector3 max)
    {
        min = max = Vector3.zero;
        if (width == 0 || height == 0 || stepX == 0f || stepZ == 0f)
            return false;

        int margin = lastShakenRing + 1;
        if (margin * 2 >= width || margin * 2 >= height)
            return false;

        min = new Vector3(
            gridOrigin.x + margin * stepX,
            gridOrigin.y,
            gridOrigin.z + margin * stepZ
        );
        max = new Vector3(
            gridOrigin.x + (width - 1 - margin) * stepX,
            gridOrigin.y,
            gridOrigin.z + (height - 1 - margin) * stepZ
        );
        return true;
    }
}
