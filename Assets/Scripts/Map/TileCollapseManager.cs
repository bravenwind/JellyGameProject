using System.Collections.Generic;
using UnityEngine;
using JellyNet;

public class TileCollapseManager : MonoBehaviour
{
    public static TileCollapseManager Instance { get; private set; }

    [SerializeField] private Transform gridParent;

    [SerializeField] private float collapseStartTime = 90f;

    [SerializeField] private float ringInterval = 15f;

    [SerializeField] private bool autoRingInterval = true;

    [SerializeField] private float endMargin = 5f;

    [SerializeField] private float groundCheckDistance = 0.6f;

    [SerializeField] private float tileDelay = 0f;

    [SerializeField] private float warningDuration = 3f;

    [SerializeField] private float fallDuration = 2f;

    [SerializeField] private float fallDistance = 30f;

    [SerializeField] private int keepCenterRings = 1;

    private const float STEP_PROCESS_INTERVAL = 0.15f;

    private const int MaxCellsPerAxis = 10000;

    private const float ThreatDistanceWeight = 1.5f;

    private const float SurvivalWeight = 40f;

    private GameObject[,] tiles;
    private int width, height;

    private int lastCollapsedRing = -1;
    private int lastShakenRing = -1;
    private Vector3 gridOrigin;
    private float stepX, stepZ;

    private HashSet<int> collapsedCells = new HashSet<int>();

    private Dictionary<int, int> tileStepCounts = new Dictionary<int, int>();
    private Dictionary<int, Color> tileOriginalColors = new Dictionary<int, Color>();

    private MaterialPropertyBlock mpb;

    private float stepProcessTimer;

    private readonly List<GameObject> carveObjects = new List<GameObject>();

    private bool ringIntervalComputed;

    private int maxSamplesPerSegment = 1;

    private bool needsStateReset = true;

    private Renderer[,] tileRenderers;

    private readonly Dictionary<int, EntityStepState> entityStates = new Dictionary<int, EntityStepState>();

    private int HighestRing
    {
        get { return Mathf.Min((width - 1) / 2, (height - 1) / 2); }
    }

    private int LastCollapsingRing
    {
        get { return HighestRing - Mathf.Max(0, keepCenterRings); }
    }

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
            return;

        ringInterval = usable / (LastCollapsingRing + 1);
    }

    private float CollapseTimeOfRing(int ring)
    {
        return collapseStartTime + (ring + 1) * ringInterval;
    }

    private float ShakeTimeOfRing(int ring)
    {
        return collapseStartTime + ring * ringInterval;
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
            return;

        width = generator.width;
        height = generator.height;

        if (width <= 0 || height <= 0)
        {
            width = height = 0;
            return;
        }

        tiles = new GameObject[width, height];
        tileRenderers = new Renderer[width, height];

        foreach (Transform child in gridParent)
        {
            if (TryParseTileName(child.name, out int x, out int z)
                && x < width && z < height)
            {
                tiles[x, z] = child.gameObject;

                tileRenderers[x, z] = child.GetComponentInChildren<Renderer>();
            }
        }

        CacheGridMetrics();
    }

    private void CacheGridMetrics()
    {
        if (tiles[0, 0] == null)
            return;

        gridOrigin = tiles[0, 0].transform.position;

        if (width > 1 && tiles[1, 0] != null)
            stepX = tiles[1, 0].transform.position.x - gridOrigin.x;

        if (height > 1 && tiles[0, 1] != null)
            stepZ = tiles[0, 1].transform.position.z - gridOrigin.z;

        if (Mathf.Approximately(stepX, 0f) || Mathf.Approximately(stepZ, 0f))
            return;

        CacheMaxPathSamples();
    }

    private void CacheMaxPathSamples()
    {
        float spanX = (width - 1) * stepX;
        float spanZ = (height - 1) * stepZ;
        float gridDiagonal = Mathf.Sqrt(spanX * spanX + spanZ * spanZ);

        maxSamplesPerSegment = Mathf.Max(1, Mathf.CeilToInt(gridDiagonal / PathSampleStep));
    }

    private float PathSampleStep
    {
        get { return Mathf.Min(stepX, stepZ) * 0.5f; }
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
            if (elapsed >= ShakeTimeOfRing(nextShakeRing))
            {
                lastShakenRing = nextShakeRing;
                StartIdleShakeOnRing(nextShakeRing);
            }
        }

        while (lastCollapsedRing < LastCollapsingRing
               && elapsed >= CollapseTimeOfRing(lastCollapsedRing + 1))
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

    private class EntityStepState
    {
        public readonly List<int> Current = new List<int>();

        public readonly List<int> Previous = new List<int>();

        public int CenterCell = -1;

        public float DwellSeconds;

        public Collider Body;
    }

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
            state.Body = entityTransform.GetComponent<CapsuleCollider>();

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
            WearTile(CellKeyToX(cellKey), CellKeyToZ(cellKey));
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
                WearTile(CellKeyToX(cellKey), CellKeyToZ(cellKey));
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

            WearTile(sweepX, sweepZ);
        }
    }

    private void WearTile(int x, int z)
    {
        if (!HasTile(x, z))
            return;

        int tileKey = CellKey(x, z);

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
        return IsCellUnsafe(worldPos, DataManager.Instance != null
            ? DataManager.Instance.StepTileDangerMargin : 1, extraSteps: 0);
    }

    public bool IsFootingUnsafe(Vector3 worldPos)
    {
        return IsCellUnsafe(worldPos, DataManager.Instance != null
            ? DataManager.Instance.StepTileFootingMargin : 1, extraSteps: 0);
    }

    // 타일이 이미 무너지는 중이거나 맵 밖이면 -1
    public int StepsAfterArrival(Vector3 worldPos)
    {
        if (stepX == 0f || stepZ == 0f)
            return 0;

        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ);

        if (x < 0 || x >= width || z < 0 || z >= height || tiles[x, z] == null)
            return -1;

        tileStepCounts.TryGetValue(CellKey(x, z), out int count);

        return Mathf.Max(0, DataManager.Instance.StepTileStepsToCollapse - count - 1);
    }

    private bool IsCellUnsafe(Vector3 worldPos, int margin, int extraSteps)
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
                if (count + extraSteps >= stepsToCollapse - margin)
                    return true;
            }

            return extraSteps >= DataManager.Instance.StepTileStepsToCollapse - margin;
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

    public bool HasPushOff(Vector3 from, Vector3 victimPos, float knockbackDistance)
    {
        if (stepX == 0f || stepZ == 0f || knockbackDistance <= 0f)
            return false;

        Vector3 dir = victimPos - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return false;
        dir.Normalize();

        float sampleStep = Mathf.Max(1f, Mathf.Min(stepX, stepZ) * 0.5f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(knockbackDistance / sampleStep));

        for (int i = 1; i <= steps; i++)
        {
            Vector3 p = victimPos + dir * (knockbackDistance * i / steps);
            if (IsOverVoid(p))
                return true;
        }

        return false;
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

    private Vector3 CellCenter(int cellX, int cellZ)
    {
        return gridOrigin + new Vector3(cellX * stepX, 0f, cellZ * stepZ);
    }

    public bool FindEscapeTile(Vector3 worldPos, Vector3 threatPos, out Vector3 safePos,
                               int searchRadiusCells = 6)
    {
        safePos = Vector3.zero;
        if (stepX == 0f || stepZ == 0f)
            return false;

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

                int stepsLeft = StepsAfterArrival(tileCenter);
                if (stepsLeft < 0)
                    continue;

                float distanceFromThreat = Vector3.Distance(tileCenter, threatPos);
                float distanceFromMe = Vector3.Distance(tileCenter, worldPos);

                float score = stepsLeft * SurvivalWeight
                            + distanceFromThreat * ThreatDistanceWeight
                            - distanceFromMe;

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

    public bool FindBestFooting(Vector3 worldPos, int searchRadiusCells,
                                System.Func<Vector3, float> score, out Vector3 best)
    {
        best = Vector3.zero;
        if (stepX == 0f || stepZ == 0f || score == null)
            return false;

        int myCellX = Mathf.Clamp(Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX), 0, width - 1);
        int myCellZ = Mathf.Clamp(Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ), 0, height - 1);

        float bestScore = float.MinValue;
        bool found = false;

        for (int offsetX = -searchRadiusCells; offsetX <= searchRadiusCells; offsetX++)
        {
            for (int offsetZ = -searchRadiusCells; offsetZ <= searchRadiusCells; offsetZ++)
            {
                if (offsetX == 0 && offsetZ == 0)
                    continue;

                int cellX = myCellX + offsetX;
                int cellZ = myCellZ + offsetZ;

                if (cellX < 0 || cellX >= width || cellZ < 0 || cellZ >= height)
                    continue;
                if (tiles[cellX, cellZ] == null)
                    continue;

                Vector3 tileCenter = CellCenter(cellX, cellZ);
                if (StepsAfterArrival(tileCenter) < 0)
                    continue;

                float value = score(tileCenter);
                if (value > bestScore)
                {
                    bestScore = value;
                    best = tileCenter;
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
                    if (Mathf.Abs(offsetX) != ringRadius && Mathf.Abs(offsetZ) != ringRadius)
                        continue;

                    int cellX = myCellX + offsetX;
                    int cellZ = myCellZ + offsetZ;

                    if (cellX < 0 || cellX >= width || cellZ < 0 || cellZ >= height)
                        continue;
                    if (tiles[cellX, cellZ] == null)
                        continue;

                    Vector3 tileCenter = CellCenter(cellX, cellZ);

                    if (avoidDangerous && StepsAfterArrival(tileCenter) <= 0)
                        continue;

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

    public bool IsPathOverCollapsing(Vector3[] corners, Vector3 startPos)
    {
        if (stepX == 0f || stepZ == 0f || corners == null || corners.Length == 0)
            return false;

        int startKey = CellKeyOf(startPos);
        float sampleStep = PathSampleStep;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 from = corners[i - 1];
            Vector3 to = corners[i];

            int steps = Mathf.Clamp(
                Mathf.CeilToInt(Vector3.Distance(from, to) / sampleStep),
                1, maxSamplesPerSegment);

            for (int j = 1; j <= steps; j++)
            {
                Vector3 p = Vector3.Lerp(from, to, (float)j / steps);

                if (CellKeyOf(p) == startKey)
                    continue;

                if (StepsAfterArrival(p) < 0)
                    return true;
            }
        }

        return false;
    }

    public bool IsPathDangerousIgnoringStart(Vector3[] corners, Vector3 startPos)
    {
        if (stepX == 0f || stepZ == 0f || corners == null || corners.Length == 0)
            return false;

        int startKey = CellKeyOf(startPos);
        float sampleStep = PathSampleStep;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 from = corners[i - 1];
            Vector3 to = corners[i];

            int steps = Mathf.Clamp(
                Mathf.CeilToInt(Vector3.Distance(from, to) / sampleStep),
                1, maxSamplesPerSegment);

            for (int j = 1; j <= steps; j++)
            {
                Vector3 p = Vector3.Lerp(from, to, (float)j / steps);

                if (CellKeyOf(p) == startKey)
                    continue;

                if (IsPositionDangerous(p))
                    return true;
            }
        }

        return false;
    }

    private int CellKeyOf(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / stepX);
        int z = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / stepZ);

        if (x < 0 || x >= width || z < 0 || z >= height)
            return int.MinValue;

        return CellKey(x, z);
    }

    public bool IsPathDangerous(Vector3[] corners)
    {
        if (stepX == 0f || stepZ == 0f || corners == null || corners.Length == 0)
            return false;

        float sampleStep = PathSampleStep;

        if (IsPositionDangerous(corners[0]))
            return true;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 from = corners[i - 1];
            Vector3 to = corners[i];

            int steps = Mathf.Clamp(
                Mathf.CeilToInt(Vector3.Distance(from, to) / sampleStep),
                1, maxSamplesPerSegment);

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
