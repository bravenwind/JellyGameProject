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

    [Tooltip("발밑이 타일 윗면보다 이만큼 이상 떠 있으면 점프로 본다(마모 안 됨). 점프 높이보다 작아야 한다.")]
    [SerializeField] private float groundCheckDistance = 0.6f;

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

    [Tooltip("가운데에 남겨둘 링 수. 마지막에 밟고 설 자리를 남기기 위한 것. 0이면 전부 무너진다.")]
    [SerializeField] private int keepCenterRings = 1;

    private int HighestRing
    {
        get { return Mathf.Min((width - 1) / 2, (height - 1) / 2); }
    }

    private int LastCollapsingRing
    {
        get { return HighestRing - Mathf.Max(0, keepCenterRings); }
    }
    private int lastCollapsedRing = -1;
    private int lastShakenRing = -1;
    private Vector3 gridOrigin;
    private float stepX, stepZ;

    private HashSet<int> collapsedCells = new HashSet<int>();

    private Dictionary<int, int> tileStepCounts = new Dictionary<int, int>();
    private Dictionary<int, Color> tileOriginalColors = new Dictionary<int, Color>();

    private MaterialPropertyBlock mpb;

    private float stepProcessTimer;
    private const float STEP_PROCESS_INTERVAL = 0.15f;

    private readonly List<GameObject> carveObjects = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else { Destroy(this); return; }

        if (gridParent == null)
            gridParent = transform;
    }

    public void RegisterCarveObject(GameObject carveObj)
    {
        if (carveObj != null)
            carveObjects.Add(carveObj);
    }

    public void ClearCarveObjects()
    {
        for (int i = 0; i < carveObjects.Count; i++)
            if (carveObjects[i] != null)
                Destroy(carveObjects[i]);
        carveObjects.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        ClearCarveObjects();
    }

    private void Start()
    {
        CollectTiles();

        if (width == 0 || height == 0)
        {
            enabled = false;

            if (Instance == this)
                Instance = null;
        }
    }

    private bool ringIntervalComputed;

    private void ComputeRingInterval()
    {
        if (!autoRingInterval || ringIntervalComputed)
            return;

        if (LastCollapsingRing < 1)
        {
            ringIntervalComputed = true;
            return;
        }

        LanGameFlow flow = LanGameFlow.Instance;

        if (flow == null || flow.GameDuration <= 0f)
            return;

        ringIntervalComputed = true;

        float duration = flow.GameDuration;
        float usable = duration - endMargin - warningDuration - fallDuration - collapseStartTime;

        if (usable <= 0f)
        {
            Debug.LogWarning($"[타일] 붕괴 시작({collapseStartTime}s)이 게임 시간({duration}s)에 비해 "
                + "너무 늦어 링을 다 못 꺼뜨립니다. collapseStartTime을 줄여주세요. 자동 계산을 끕니다.");
            return;
        }

        ringInterval = usable / LastCollapsingRing;

        Debug.Log($"[타일] 링 0~{HighestRing} 중 0~{LastCollapsingRing}이 무너짐"
            + $"(가운데 {keepCenterRings}겹은 남김) · 게임 {duration}s → 간격 {ringInterval:F2}s "
            + $"(마지막 링 완료 {duration - endMargin:F1}s 지점)");
    }

    private bool IsRunning()
    {
        var flow = LanGameFlow.Instance;
        return flow != null && flow.Phase == GamePhase.Playing;
    }

    private void CollectTiles()
    {
        AutoGridMapGenerator generator = gridParent.GetComponent<AutoGridMapGenerator>();

        if (generator == null)
        {
            Debug.LogError($"[타일] {gridParent.name}에 AutoGridMapGenerator가 없습니다. "
                + "격자 크기를 알 수 없어 붕괴 시스템을 켜지 않습니다.");
            return;
        }

        width = generator.width;
        height = generator.height;

        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"[타일] 격자 크기가 {width}x{height}입니다 — 붕괴 시스템을 켜지 않습니다.");
            width = height = 0;
            return;
        }

        tiles = new GameObject[width, height];
        tileRenderers = new Renderer[width, height];

        int found = 0;

        foreach (Transform child in gridParent)
        {
            if (TryParseTileName(child.name, out int x, out int z)
                && x < width && z < height)
            {
                tiles[x, z] = child.gameObject;

                tileRenderers[x, z] = child.GetComponentInChildren<Renderer>();
                found++;
            }
        }

        if (found != width * height)
            Debug.LogWarning($"[타일] {width}x{height} = {width * height}칸인데 {found}개만 찾았습니다 — 빠진 타일이 있습니다.");

        CacheGridMetrics();
    }

    private void CacheGridMetrics()
    {
        if (tiles[0, 0] == null)
        {
            Debug.LogError("[타일] Tile_0_0이 없습니다 — 격자의 원점을 잡을 수 없어 밟기 판정이 동작하지 않습니다.");
            return;
        }

        gridOrigin = tiles[0, 0].transform.position;

        if (width > 1 && tiles[1, 0] != null)
            stepX = tiles[1, 0].transform.position.x - gridOrigin.x;

        if (height > 1 && tiles[0, 1] != null)
            stepZ = tiles[0, 1].transform.position.z - gridOrigin.z;

        if (Mathf.Approximately(stepX, 0f) || Mathf.Approximately(stepZ, 0f))
        {
            Debug.LogError($"[타일] 타일 간격을 잴 수 없습니다 (stepX={stepX}, stepZ={stepZ}). "
                + "Tile_1_0 · Tile_0_1이 제자리에 있는지 확인하세요 — 밟기 판정이 동작하지 않습니다.");
            return;
        }

        CacheMaxPathSamples();
    }

    /// <summary>
    /// 경로 한 구간을 최대 몇 등분까지 검사할지. 격자에서 유도한다.
    ///
    /// ★ 예전엔 상수 16이었는데, 그 값이 검사를 무력화하고 있었다
    ///   구간 하나는 격자를 대각선으로 가로지르는 길이까지 나올 수 있다
    ///   (평평한 격자라 장애물이 없으면 NavMesh가 코너 두 개짜리 직선을 준다).
    ///   이 맵은 대각선이 약 308m인데 16등분이면 샘플 간격이 19m가 된다.
    ///   칸이 14m니까 <b>칸 하나가 통째로 건너뛰어졌다</b> — 위험한 칸을 지나는 경로가
    ///   안전 판정을 받고 통과했다는 뜻이다.
    ///
    ///   가장 긴 구간을 목표 간격으로 쪼갤 수 있는 수를 그대로 상한으로 쓴다.
    ///   그러면 상한에 걸려도 간격 보장이 깨지지 않는다.
    /// </summary>
    private void CacheMaxPathSamples()
    {
        float spanX = (width - 1) * stepX;
        float spanZ = (height - 1) * stepZ;
        float gridDiagonal = Mathf.Sqrt(spanX * spanX + spanZ * spanZ);

        maxSamplesPerSegment = Mathf.Max(1, Mathf.CeilToInt(gridDiagonal / PathSampleStep));
    }

    /// <summary>샘플 사이 목표 간격. 반 칸이면 칸을 건너뛸 일이 없다.</summary>
    private float PathSampleStep
    {
        get { return Mathf.Min(stepX, stepZ) * 0.5f; }
    }

    private int maxSamplesPerSegment = 1;

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

    private bool needsStateReset = true;

    private void Update()
    {
        if (!IsRunning())
            return;

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            UpdateStepCollapse();
            return;
        }

        ComputeRingInterval();

        float elapsed = LanGameFlow.SyncedElapsed;
        if (elapsed < 0f)
            return;

        int nextShakeRing = lastCollapsedRing + 1;
        if (nextShakeRing <= LastCollapsingRing && nextShakeRing > lastShakenRing)
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
        targetRing = Mathf.Min(targetRing, LastCollapsingRing);

        while (lastCollapsedRing < targetRing)
        {
            lastCollapsedRing++;
            CollapseRingAnimated(lastCollapsedRing);
        }
    }

    private void UpdateStepCollapse()
    {
        if (NetManager.Instance != null
            && !NetManager.Offline
            && !NetManager.Instance.IsHost) return;

        if (stepX == 0f || stepZ == 0f)
            return;

        if (needsStateReset)
        {
            ResetEntityStates();
            needsStateReset = false;
            stepProcessTimer = 0f;
            return;
        }

        stepProcessTimer += Time.deltaTime;
        if (stepProcessTimer < STEP_PROCESS_INTERVAL)
            return;
        float dt = stepProcessTimer;
        stepProcessTimer = 0f;

        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null)
                continue;

            if (e.Transform == null || e.IsOutOfPlay)
            {
                entityStates.Remove(e.EntityId);
                continue;
            }

            TryStepAt(e.Transform, e.EntityId, e.ScaleValue, dt);
        }
    }

    private Renderer[,] tileRenderers;

    private class EntityStepState
    {
        public readonly List<int> Current = new List<int>();

        public readonly List<int> Previous = new List<int>();

        public int CenterCell = -1;

        public float DwellSeconds;

        public Collider Body;
    }

    private readonly Dictionary<int, EntityStepState> entityStates = new Dictionary<int, EntityStepState>();

    private EntityStepState StateOf(int entityId)
    {
        if (!entityStates.TryGetValue(entityId, out EntityStepState state))
        {
            state = new EntityStepState();
            entityStates[entityId] = state;
        }

        return state;
    }

    private float FeetYOf(Transform entityTransform, EntityStepState state)
    {
        if (state.Body == null)
        {
            state.Body = entityTransform.GetComponent<CapsuleCollider>();
        }

        return state.Body != null ? state.Body.bounds.min.y : entityTransform.position.y;
    }

    private bool IsStandingOn(int x, int z, float feetY)
    {
        Renderer tileRenderer = tileRenderers[x, z];

        if (tileRenderer == null)
            return false;

        float heightAboveTile = feetY - tileRenderer.bounds.max.y;

        return heightAboveTile <= groundCheckDistance;
    }

    private const int MaxCellsPerAxis = 10000;

    private static int CellKey(int x, int z)
    {
        return x * MaxCellsPerAxis + z;
    }

    private static int CellKeyToX(int key)
    {
        return key / MaxCellsPerAxis;
    }

    private static int CellKeyToZ(int key)
    {
        return key % MaxCellsPerAxis;
    }

    private void CollectFootprint(Transform entityTransform, EntityStepState state, float scale)
    {
        List<int> into = state.Current;
        into.Clear();

        Vector3 worldPos = entityTransform.position;

        float feetY = FeetYOf(entityTransform, state);

        float bodyRadiusMeters = Mathf.Max(0.01f, NavMeshUtil.PlayerJellyRadius * Mathf.Max(0.01f, scale));

        float bodyCenterCellX = (worldPos.x - gridOrigin.x) / stepX;
        float bodyCenterCellZ = (worldPos.z - gridOrigin.z) / stepZ;

        float bodyRadiusCellsX = bodyRadiusMeters / stepX;
        float bodyRadiusCellsZ = bodyRadiusMeters / stepZ;

        int searchMinX = Mathf.FloorToInt(bodyCenterCellX - bodyRadiusCellsX);
        int searchMaxX = Mathf.CeilToInt(bodyCenterCellX + bodyRadiusCellsX);
        int searchMinZ = Mathf.FloorToInt(bodyCenterCellZ - bodyRadiusCellsZ);
        int searchMaxZ = Mathf.CeilToInt(bodyCenterCellZ + bodyRadiusCellsZ);

        for (int cellX = searchMinX; cellX <= searchMaxX; cellX++)
        {
            for (int cellZ = searchMinZ; cellZ <= searchMaxZ; cellZ++)
            {
                if (!HasTile(cellX, cellZ))
                    continue;

                float closestPointCellX = Mathf.Clamp(bodyCenterCellX, cellX - 0.5f, cellX + 0.5f);
                float closestPointCellZ = Mathf.Clamp(bodyCenterCellZ, cellZ - 0.5f, cellZ + 0.5f);

                float gapMetersX = (bodyCenterCellX - closestPointCellX) * stepX;
                float gapMetersZ = (bodyCenterCellZ - closestPointCellZ) * stepZ;

                float gapSquared = gapMetersX * gapMetersX + gapMetersZ * gapMetersZ;

                if (gapSquared > bodyRadiusMeters * bodyRadiusMeters)
                    continue;

                if (!IsStandingOn(cellX, cellZ, feetY))
                    continue;

                into.Add(CellKey(cellX, cellZ));
            }
        }
    }

    private void TryStepAt(Transform entityTransform, int entityId, float scale, float dt)
    {
        Vector3 worldPos = entityTransform.position;
        EntityStepState state = StateOf(entityId);

        CollectFootprint(entityTransform, state, scale);

        List<int> current = state.Current;
        List<int> previous = state.Previous;

        if (current.Count == 0)
        {
            state.DwellSeconds = 0f;
            return;
        }

        bool footprintChanged = false;

        for (int i = 0; i < current.Count; i++)
        {
            int cellKey = current[i];

            if (previous.Contains(cellKey))
                continue;

            footprintChanged = true;
            WearTile(CellKeyToX(cellKey), CellKeyToZ(cellKey), cellKey);
        }

        if (!footprintChanged && previous.Count != current.Count)
            footprintChanged = true;

        int currentCenterKey = NearestFootprintCell(current,
            (worldPos.x - gridOrigin.x) / stepX,
            (worldPos.z - gridOrigin.z) / stepZ);

        if (state.CenterCell >= 0 && state.CenterCell != currentCenterKey)
            WearSkippedCells(state.CenterCell, currentCenterKey);

        state.CenterCell = currentCenterKey;

        previous.Clear();
        previous.AddRange(current);

        if (footprintChanged)
        {
            state.DwellSeconds = 0f;
            return;
        }

        DataManager rules = DataManager.Instance;
        float secondsPerIdleWear = rules != null ? rules.StepTileIdleWearSeconds : 0f;

        if (secondsPerIdleWear <= 0f)
            return;

        float dwellSeconds = state.DwellSeconds + dt;

        if (dwellSeconds >= secondsPerIdleWear)
        {
            dwellSeconds -= secondsPerIdleWear;

            for (int i = 0; i < current.Count; i++)
            {
                int cellKey = current[i];
                WearTile(CellKeyToX(cellKey), CellKeyToZ(cellKey), cellKey);
            }
        }

        state.DwellSeconds = dwellSeconds;
    }

    private int NearestFootprintCell(List<int> footprint, float bodyCenterCellX, float bodyCenterCellZ)
    {
        int nearestKey = footprint[0];
        float nearestDistanceSquared = float.MaxValue;

        for (int i = 0; i < footprint.Count; i++)
        {
            int key = footprint[i];

            float offsetX = bodyCenterCellX - CellKeyToX(key);
            float offsetZ = bodyCenterCellZ - CellKeyToZ(key);

            float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;

            if (distanceSquared >= nearestDistanceSquared)
                continue;

            nearestDistanceSquared = distanceSquared;
            nearestKey = key;
        }

        return nearestKey;
    }

    private bool HasTile(int x, int z)
    {
        return x >= 0 && x < width && z >= 0 && z < height && tiles[x, z] != null;
    }

    private void ResetEntityStates()
    {
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null || e.IsOutOfPlay)
                continue;
            ResetEntityState(e.Transform, e.EntityId, e.ScaleValue);
        }
    }

    private void ResetEntityState(Transform entityTransform, int entityId, float scale)
    {
        Vector3 worldPos = entityTransform.position;
        EntityStepState state = StateOf(entityId);

        CollectFootprint(entityTransform, state, scale);

        if (state.Current.Count == 0)
            return;

        state.Previous.Clear();
        state.Previous.AddRange(state.Current);

        state.CenterCell = NearestFootprintCell(state.Current,
            (worldPos.x - gridOrigin.x) / stepX,
            (worldPos.z - gridOrigin.z) / stepZ);

        state.DwellSeconds = 0f;
    }

    private void WearSkippedCells(int fromKey, int toKey)
    {
        int fromX = CellKeyToX(fromKey);
        int fromZ = CellKeyToZ(fromKey);
        int toX = CellKeyToX(toKey);
        int toZ = CellKeyToZ(toKey);

        int distanceX = Mathf.Abs(toX - fromX);
        int distanceZ = Mathf.Abs(toZ - fromZ);

        const int MaxSweepCells = 8;
        int manhattanDistance = distanceX + distanceZ;

        if (manhattanDistance <= 1 || manhattanDistance > MaxSweepCells)
            return;

        int stepCount = Mathf.Max(distanceX, distanceZ);

        for (int step = 1; step < stepCount; step++)
        {
            float progress = (float)step / stepCount;

            int sweepX = Mathf.RoundToInt(Mathf.Lerp(fromX, toX, progress));
            int sweepZ = Mathf.RoundToInt(Mathf.Lerp(fromZ, toZ, progress));

            if (!HasTile(sweepX, sweepZ))
                continue;

            WearTile(sweepX, sweepZ, CellKey(sweepX, sweepZ));
        }
    }

    private void WearTile(int x, int z, int tileKey)
    {
        if (tiles[x, z] == null)
            return;

        tileStepCounts.TryGetValue(tileKey, out int count);
        count++;
        tileStepCounts[tileKey] = count;

        int stepsToCollapse = DataManager.Instance.StepTileStepsToCollapse;

        if (NetWorld.Instance == null)
            return;

        if (count >= stepsToCollapse)
            CollapseStepTile(x, z, broadcast: true);
        else
            BroadcastDarken(x, z, count, stepsToCollapse);
    }

    private void BroadcastDarken(int x, int z, int stepCount, int stepsToCollapse)
    {
        DarkenStepTile(x, z, stepCount, stepsToCollapse);
        NetWorld.Instance.BroadcastTileWear(x, z, stepCount, stepsToCollapse);
    }

    public void DarkenStepTile(int x, int z, int stepCount, int stepsToCollapse)
    {
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        if (tiles[x, z] == null)
            return;

        int tileKey = CellKey(x, z);

        tileStepCounts[tileKey] = stepCount;

        Renderer rend = tiles[x, z].GetComponentInChildren<Renderer>();

        if (!TileColorProps.HasColor(rend))
            return;

        if (!tileOriginalColors.TryGetValue(tileKey, out Color original))
        {
            original = rend.sharedMaterial.GetColor(TileColorProps.BaseColorId);
            tileOriginalColors[tileKey] = original;
        }

        float wearRatio = (float)stepCount / stepsToCollapse;
        Color danger = new Color(original.r * 0.3f, original.g * 0.15f, original.b * 0.1f);

        if (mpb == null)
            mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetColor(TileColorProps.BaseColorId, Color.Lerp(original, danger, wearRatio));
        rend.SetPropertyBlock(mpb);
    }

    public void CollapseStepTile(int x, int z, bool broadcast)
    {
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        if (tiles[x, z] == null)
            return;

        if (broadcast && NetWorld.Instance != null)
            NetWorld.Instance.BroadcastTileCollapse(x, z);

        DataManager rules = DataManager.Instance;
        float warningSeconds = rules.StepTileWarningDuration;
        float collapseDelaySeconds = rules.StepTileCollapseDelay;

        FallingTile fallingTile = tiles[x, z].GetComponent<FallingTile>();

        if (fallingTile == null)
            fallingTile = tiles[x, z].AddComponent<FallingTile>();

        fallingTile.SetGridPos(x, z);
        fallingTile.StartFall(warningSeconds, fallDuration, fallDistance,
            Mathf.Max(0f, collapseDelaySeconds - warningSeconds));
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
                    FallingTile fallingTile = tiles[x, z].GetComponent<FallingTile>();

                    if (fallingTile == null)
                        fallingTile = tiles[x, z].AddComponent<FallingTile>();

                    fallingTile.StartIdleShake();
                }
            }
        }
    }

    private void CollapseRingAnimated(int ring)
    {
        float chainDelay = 0f;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (GetRing(x, z) == ring && tiles[x, z] != null)
                {
                    FallingTile fallingTile = tiles[x, z].GetComponent<FallingTile>();

                    if (fallingTile == null)
                        fallingTile = tiles[x, z].AddComponent<FallingTile>();

                    fallingTile.SetGridPos(x, z);
                    fallingTile.StartFall(warningDuration, fallDuration, fallDistance, chainDelay);
                    tiles[x, z] = null;
                    if (tileDelay > 0f)
                        chainDelay += tileDelay;
                }
            }
        }
    }

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
            int tileKey = CellKey(x, z);
            if (tileStepCounts.TryGetValue(tileKey, out int count))
            {
                int stepsToCollapse = DataManager.Instance.StepTileStepsToCollapse;
                if (count >= stepsToCollapse - 1)
                    return true;
            }
            return false;
        }

        int ring = GetRing(x, z);
        return ring <= lastShakenRing;
    }

    public void MarkCellCollapsed(int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height)
            return;
        collapsedCells.Add(CellKey(x, z));
    }

    public bool IsOverVoid(Vector3 worldPos)
    {
        if (stepX == 0f || stepZ == 0f)
            return false;

        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ);

        if (x < 0 || x >= width || z < 0 || z >= height)
            return true;
        return collapsedCells.Contains(CellKey(x, z));
    }

    /// <summary>격자 칸 하나의 월드 좌표. 발판 위치를 묻는 곳은 전부 여기로 온다.</summary>
    private Vector3 CellCenter(int cellX, int cellZ)
    {
        return gridOrigin + new Vector3(cellX * stepX, 0f, cellZ * stepZ);
    }

    /// <summary>
    /// 위협에게서 도망칠 발판을 고른다. 밀치기 모드에서 봇이 무너지는 칸에 섰을 때 쓴다.
    ///
    /// ★ '가까운 안전 칸'이 아니라 '위협에서 먼 안전 칸'이다
    ///   가까운 곳만 찾으면 무너지는 발판은 피했는데 때리려는 사람 품으로 뛰어든다.
    ///   그래서 FindNearestSafeTile처럼 가까운 고리부터 찾다 멈추지 않고,
    ///   주변 (2 × 반지름 + 1)² 칸을 전수 조사해 가장 점수 높은 한 칸을 고른다.
    /// </summary>
    public bool FindEscapeTile(Vector3 worldPos, Vector3 threatPos, out Vector3 safePos,
                               int searchRadiusCells = 6)
    {
        safePos = Vector3.zero;
        if (stepX == 0f || stepZ == 0f)
            return false;

        // 월드 좌표 → 칸 번호. Clamp는 봇이 격자 밖으로 튕겨 나갔을 때 가장자리로 붙잡아 둔다.
        int myCellX = Mathf.Clamp(Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX), 0, width - 1);
        int myCellZ = Mathf.Clamp(Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ), 0, height - 1);

        float bestScore = float.MinValue;
        bool found = false;

        for (int offsetX = -searchRadiusCells; offsetX <= searchRadiusCells; offsetX++)
        {
            for (int offsetZ = -searchRadiusCells; offsetZ <= searchRadiusCells; offsetZ++)
            {
                int cellX = myCellX + offsetX;
                int cellZ = myCellZ + offsetZ;

                if (cellX < 0 || cellX >= width || cellZ < 0 || cellZ >= height)
                    continue;
                if (tiles[cellX, cellZ] == null)
                    continue;

                Vector3 tileCenter = CellCenter(cellX, cellZ);
                if (IsPositionDangerous(tileCenter))
                    continue;

                float distanceFromThreat = Vector3.Distance(tileCenter, threatPos);
                float distanceFromMe = Vector3.Distance(tileCenter, worldPos);

                // ★ 두 힘의 줄다리기
                //   앞 항: 위협에서 멀수록 가점 — 멀리 도망가고 싶다
                //   뒷 항: 나에게서 멀수록 감점 — 너무 멀면 가는 도중에 발판이 꺼진다
                //   1.5는 "위협에서 1m 더 멀어지는 것"이 "내가 1m 더 뛰는 것"보다
                //   1.5배 가치 있다는 뜻이다. 1보다 작으면 코앞 칸만 골라 붙잡히고,
                //   너무 크면 맵 반대편까지 무작정 뛴다.
                float score = distanceFromThreat * ThreatDistanceWeight - distanceFromMe;

                if (score > bestScore)
                {
                    bestScore = score;
                    safePos = tileCenter;
                    found = true;
                }
            }
        }

        return found;
    }

    private const float ThreatDistanceWeight = 1.5f;

    /// <summary>
    /// 가장 가까운 발판을 고른다. "일단 설 곳"이 필요할 때의 폴백이다.
    ///
    /// 안쪽 고리부터 한 겹씩 넓혀 가며 찾고, 발판을 하나라도 찾은 고리에서 멈춘다.
    /// 그 고리 안에서만 최단거리를 비교하므로 더 바깥은 볼 필요가 없다.
    /// </summary>
    public bool FindNearestSafeTile(Vector3 worldPos, out Vector3 safePos, bool avoidDangerous = false)
    {
        safePos = Vector3.zero;
        if (stepX == 0f || stepZ == 0f)
            return false;

        int myCellX = Mathf.Clamp(Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX), 0, width - 1);
        int myCellZ = Mathf.Clamp(Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ), 0, height - 1);

        int maxRingRadius = Mathf.Max(width, height);

        for (int ringRadius = 1; ringRadius <= maxRingRadius; ringRadius++)
        {
            float bestSqrDistance = float.MaxValue;
            bool found = false;

            for (int offsetX = -ringRadius; offsetX <= ringRadius; offsetX++)
            {
                for (int offsetZ = -ringRadius; offsetZ <= ringRadius; offsetZ++)
                {
                    // 정사각형의 <b>테두리</b>만 본다. 둘 다 반지름보다 작으면 안쪽 칸이고,
                    // 안쪽은 이미 지난 고리에서 봤다.
                    if (Mathf.Abs(offsetX) != ringRadius && Mathf.Abs(offsetZ) != ringRadius)
                        continue;

                    int cellX = myCellX + offsetX;
                    int cellZ = myCellZ + offsetZ;

                    if (cellX < 0 || cellX >= width || cellZ < 0 || cellZ >= height)
                        continue;
                    if (tiles[cellX, cellZ] == null)
                        continue;

                    Vector3 tileCenter = CellCenter(cellX, cellZ);

                    if (avoidDangerous && IsPositionDangerous(tileCenter))
                        continue;

                    // 제곱거리로 비교한다. 크기 순서는 같고 제곱근을 뽑지 않아도 된다.
                    float sqrDistance = (tileCenter - worldPos).sqrMagnitude;
                    if (sqrDistance < bestSqrDistance)
                    {
                        bestSqrDistance = sqrDistance;
                        safePos = tileCenter;
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
    /// 이 경로가 무너지거나 무너질 칸을 지나가는지.
    ///
    /// ★ 개수를 인자로 받지 않는다
    ///   예전엔 (corners, count) 두 개를 받았고 호출부는 전부 corners.Length를 넘겼다.
    ///   그런데 NavMeshPath.corners는 필드가 아니라 <b>접근할 때마다 배열을 새로 만드는
    ///   프로퍼티</b>다. 그래서 호출 한 번에 배열이 두 개씩 생기고 있었다 —
    ///   하나는 내용을 쓰려고, 하나는 Length 하나 읽자고.
    ///
    ///   개수를 따로 받는 게 옳은 경우는 GetCornersNonAlloc처럼 큰 버퍼의 앞부분만
    ///   채우는 때다. 그때는 Length가 용량이지 유효 개수가 아니다. 여기는 그 경우가 아니다.
    /// </summary>
    public bool IsPathDangerous(Vector3[] corners)
    {
        if (stepX == 0f || stepZ == 0f || corners == null || corners.Length == 0)
            return false;

        float sampleStep = PathSampleStep;

        // 첫 코너는 어느 구간의 끝점도 아니라서 여기서 따로 본다.
        // 나머지 코너는 전부 어떤 구간의 to로 아래에서 검사된다.
        if (IsPositionDangerous(corners[0]))
            return true;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 from = corners[i - 1];
            Vector3 to = corners[i];

            // sampleStep은 '간격'(미터), steps는 '등분 수'(개). 간격을 목표 이하로
            // 만들려면 몇 등분해야 하는지를 올림으로 구한다.
            // 하한 1은 길이 0인 구간(코너가 겹쳐 나오는 경우)에서도 to를 한 번은
            // 보게 한다 — 0등분이면 아래 루프가 안 돌아 그 코너가 검사에서 빠진다.
            int steps = Mathf.Clamp(
                Mathf.CeilToInt(Vector3.Distance(from, to) / sampleStep),
                1, maxSamplesPerSegment);

            // j가 0이 아니라 1부터인 이유: t=0은 from인데, 그건 직전 구간의 to로
            // 이미 봤다. t는 1/steps에서 시작해 정확히 1(=to)로 끝난다.
            // (float) 캐스팅이 없으면 정수 나눗셈이라 t가 마지막만 1이고 전부 0이 된다.
            for (int j = 1; j <= steps; j++)
            {
                if (IsPositionDangerous(Vector3.Lerp(from, to, (float)j / steps)))
                    return true;
            }
        }

        return false;
    }

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
