using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class TileCollapseManager : MonoBehaviour
{
    public static TileCollapseManager Instance { get; private set; }

    [Header("Grid")]
    [Tooltip("타일들의 부모 Transform (비워두면 이 오브젝트에서 탐색)")]
    public Transform gridParent;

    [Header("붕괴 타이밍")]
    [Tooltip("게임 시작 후 붕괴 시작까지의 시간 (초)")]
    public float collapseStartTime = 90f;

    [Tooltip("각 링 붕괴 간격 (초)")]
    public float ringInterval = 15f;

    [Tooltip("같은 링 내 타일 간 연쇄 딜레이 (초) — 0이면 동시에 떨어짐")]
    public float tileDelay = 0f;

    [Header("타일 애니메이션")]
    [Tooltip("경고 흔들림 시간 (초)")]
    public float warningDuration = 3f;

    [Tooltip("떨어지는 시간 (초)")]
    public float fallDuration = 2f;

    [Tooltip("떨어지는 거리")]
    public float fallDistance = 30f;

    private GameObject[,] _tiles;
    private int _width, _height;
    private int _maxRing;
    private int _lastCollapsedRing = -1;
    private int _lastShakenRing = -1;
    private Vector3 _gridOrigin;
    private float _stepX, _stepZ;

    // 실제로 붕괴(carve)가 끝나 발판이 사라진 칸들. IsOverVoid 판정용.
    private HashSet<int> _collapsedCells = new HashSet<int>();

    private Dictionary<int, int> _tileStepCounts = new Dictionary<int, int>();
    private Dictionary<int, int> _entityCurrentTile = new Dictionary<int, int>();
    private Dictionary<int, float> _entityDwellTime = new Dictionary<int, float>();
    private Dictionary<int, Color> _tileOriginalColors = new Dictionary<int, Color>();

    // 경고 색 변경용 셰이더 프로퍼티(URP=_BaseColor / 빌트인=_Color). MaterialPropertyBlock으로
    // 칠하면 .material처럼 인스턴스를 복제하지 않아 배칭이 유지된다. (K1 — FallingTile G3와 동일 패턴)
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock _mpb;

    private float _stepProcessTimer;
    private const float STEP_PROCESS_INTERVAL = 0.15f;

    // FallingTile이 붕괴 시 루트에 생성하는 NavCarve_* 장애물들. 구멍을 유지하기 위해 한 판 동안은
    // 살려 두지만, 매니저가 소유 목록으로 들고 있다가 라운드/씬 종료 시 일괄 정리한다. (G7)
    private readonly List<GameObject> _carveObjects = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }

        if (gridParent == null) gridParent = transform;
    }

    /// <summary>FallingTile이 만든 carve 장애물을 매니저 소유로 등록한다. (G7)</summary>
    public void RegisterCarveObject(GameObject carveObj)
    {
        if (carveObj != null) _carveObjects.Add(carveObj);
    }

    /// <summary>등록된 carve 장애물을 모두 파괴하고 목록을 비운다(라운드 종료/씬 정리용). (G7)</summary>
    public void ClearCarveObjects()
    {
        for (int i = 0; i < _carveObjects.Count; i++)
            if (_carveObjects[i] != null) Destroy(_carveObjects[i]);
        _carveObjects.Clear();
    }

    private void OnDestroy()
    {
        // 씬이 carve 오브젝트를 자동 정리하긴 하지만, 매니저가 소유한 만큼 명시적으로 정리해
        // 누수/잔존(추가 씬 구성 등)을 차단한다. (G7)
        if (Instance == this) Instance = null;
        ClearCarveObjects();
    }

    private void Start()
    {
        CollectTiles();
        if (_width == 0 || _height == 0) return;
        _maxRing = Mathf.Min(_width, _height) / 2;
    }

    private float GetSyncedElapsed()
    {
        if (GameModeManager.Instance == null) return -1f;
        float networked = GameModeManager.Instance.NetworkedElapsedTime;
        if (networked >= 0f) return networked;
        return GameModeManager.Instance.SurvivedTime;
    }

    private void CollectTiles()
    {
        var gen = gridParent.GetComponent<AutoGridMapGenerator>();
        if (gen != null)
        {
            _width = gen.width;
            _height = gen.height;
        }
        else
        {
            int maxX = 0, maxZ = 0;
            foreach (Transform child in gridParent)
            {
                if (TryParseTileName(child.name, out int x, out int z))
                {
                    if (x > maxX) maxX = x;
                    if (z > maxZ) maxZ = z;
                }
            }
            _width = maxX + 1;
            _height = maxZ + 1;
        }

        _tiles = new GameObject[_width, _height];
        foreach (Transform child in gridParent)
        {
            if (TryParseTileName(child.name, out int x, out int z)
                && x < _width && z < _height)
            {
                _tiles[x, z] = child.gameObject;
            }
        }

        // 월드 → 타일 좌표 변환용 원점/간격 캐시
        if (_width > 0 && _height > 0 && _tiles[0, 0] != null)
            _gridOrigin = _tiles[0, 0].transform.position;
        if (_width > 1 && _tiles[1, 0] != null && _tiles[0, 0] != null)
            _stepX = _tiles[1, 0].transform.position.x - _tiles[0, 0].transform.position.x;
        if (_height > 1 && _tiles[0, 1] != null && _tiles[0, 0] != null)
            _stepZ = _tiles[0, 1].transform.position.z - _tiles[0, 0].transform.position.z;
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
        return Mathf.Min(x, z, _width - 1 - x, _height - 1 - z);
    }

    // [MAP-1] 마스터 승계 감지. 새로 마스터가 된 클라는 _entityCurrentTile/_entityDwellTime
    // (체류 마모 상태)가 비어 있어, 그대로 두면 첫 UpdateStepCollapse에서 맵 위 전 개체의
    // "이전 칸 없음(-1)→현재 칸" 전이로 오인해 모두의 현재 타일을 동시에 한 번씩 마모시킨다
    // (임계 직전 타일들이 마스터 교체 순간 동시다발 붕괴). 승계 직후 첫 패스는 마모 없이
    // 현재 위치만 시딩해, 그 다음부터 정상적인 '이동 감지' 마모가 이뤄지게 한다.
    // (_tileStepCounts 자체는 X3로 RPC를 통해 전 클라에 복제되므로 마모 카운트는 승계됨.)
    private bool _wasMaster = false;
    private bool _needsStepGrace = false;

    private void Update()
    {
        bool isMaster = PhotonNetwork.IsMasterClient;
        if (isMaster && !_wasMaster) _needsStepGrace = true; // 방금 마스터가 됨(게임 시작 포함)
        _wasMaster = isMaster;

        if (GameModeManager.Instance == null || !GameModeManager.Instance.IsGameRunning) return;

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            UpdateStepCollapse();
            return;
        }

        float elapsed = GetSyncedElapsed();
        if (elapsed < 0f) return;

        int nextShakeRing = _lastCollapsedRing + 1;
        if (nextShakeRing < _maxRing && nextShakeRing > _lastShakenRing)
        {
            float shakeStartTime = collapseStartTime + (nextShakeRing - 1) * ringInterval;
            if (elapsed >= shakeStartTime)
            {
                _lastShakenRing = nextShakeRing;
                StartIdleShakeOnRing(nextShakeRing);
            }
        }

        if (elapsed < collapseStartTime) return;

        int targetRing = Mathf.FloorToInt((elapsed - collapseStartTime) / ringInterval);
        targetRing = Mathf.Min(targetRing, _maxRing - 1);

        while (_lastCollapsedRing < targetRing)
        {
            _lastCollapsedRing++;
            CollapseRingAnimated(_lastCollapsedRing);
        }
    }

    private void UpdateStepCollapse()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (_stepX == 0f || _stepZ == 0f) return;

        // [MAP-1] 마스터 승계 직후 그레이스: 현재 위치만 시딩하고 이번 패스는 마모하지 않는다.
        if (_needsStepGrace)
        {
            SeedEntityTilesNoWear();
            _needsStepGrace = false;
            _stepProcessTimer = 0f;
            return;
        }

        _stepProcessTimer += Time.deltaTime;
        if (_stepProcessTimer < STEP_PROCESS_INTERVAL) return;
        float dt = _stepProcessTimer;
        _stepProcessTimer = 0f;

        foreach (var player in EntityRegistry.Players)
        {
            if (player == null || player.IsOutOfPlay) continue; // 탈락/흡수 판정 단일 출처 (G6/K2)
            TryStepAt(player.transform.position, player.photonView.ViewID, dt);
        }

        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || bot.IsEliminated) continue;
            TryStepAt(bot.transform.position, bot.photonView.ViewID, dt);
        }
    }

    // [MAP-1] 현재 각 개체가 서 있는 칸을 마모 없이 기록만 한다(마스터 승계 그레이스용).
    private void SeedEntityTilesNoWear()
    {
        foreach (var player in EntityRegistry.Players)
        {
            if (player == null || player.IsOutOfPlay) continue;
            SeedTile(player.transform.position, player.photonView.ViewID);
        }
        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || bot.IsEliminated) continue;
            SeedTile(bot.transform.position, bot.photonView.ViewID);
        }
    }

    private void SeedTile(Vector3 worldPos, int entityID)
    {
        int x = Mathf.RoundToInt((worldPos.x - _gridOrigin.x) / _stepX);
        int z = Mathf.RoundToInt((worldPos.z - _gridOrigin.z) / _stepZ);
        if (x < 0 || x >= _width || z < 0 || z >= _height) return;
        _entityCurrentTile[entityID] = x * 10000 + z;
        _entityDwellTime[entityID] = 0f;
    }

    private void TryStepAt(Vector3 worldPos, int entityID, float dt)
    {
        int x = Mathf.RoundToInt((worldPos.x - _gridOrigin.x) / _stepX);
        int z = Mathf.RoundToInt((worldPos.z - _gridOrigin.z) / _stepZ);

        if (x < 0 || x >= _width || z < 0 || z >= _height) return;
        if (_tiles[x, z] == null) return;

        int tileKey = x * 10000 + z;

        // 새 타일로 이동: 마지막 타일에서 현재 타일까지의 경로상 칸들을 마모시키고 체류 타이머 초기화.
        // (현재 위치만 0.15초마다 샘플링하므로, 대쉬 등으로 빠르게 지나가면 중간 칸이 통째로 건너뛰어져
        //  '밟아도 안 떨어지는' 현상이 생긴다. 마지막→현재 칸을 라인으로 훑어 건너뛴 칸도 마모한다.)
        if (!_entityCurrentTile.TryGetValue(entityID, out int lastTile) || lastTile != tileKey)
        {
            if (!_entityCurrentTile.ContainsKey(entityID)) lastTile = -1;
            WearTilePath(lastTile, x, z, tileKey);
            _entityCurrentTile[entityID] = tileKey;
            _entityDwellTime[entityID] = 0f;
            return;
        }

        // 같은 타일에 계속 머무름: 일정 시간 이상 머물면 추가로 마모(견디는 횟수 감소)
        var dm = DataManager.Instance;
        float idleWear = dm != null ? dm.stepTileIdleWearSeconds : 0f;
        if (idleWear <= 0f) return;

        _entityDwellTime.TryGetValue(entityID, out float dwell);
        dwell += dt;
        if (dwell >= idleWear)
        {
            dwell -= idleWear; // 초과분 보존 → 이후에도 idleWear마다 계속 마모
            WearTile(x, z, tileKey);
        }
        _entityDwellTime[entityID] = dwell;
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
            if (xi < 0 || xi >= _width || zi < 0 || zi >= _height) continue;
            if (_tiles[xi, zi] == null) continue;
            WearTile(xi, zi, xi * 10000 + zi);
        }
    }

    /// <summary>타일 견디는 횟수를 1 소모시키고, 한계 도달 시 붕괴/아니면 색 어둡게 RPC 전파.</summary>
    private void WearTile(int x, int z, int tileKey)
    {
        if (_tiles[x, z] == null) return;

        _tileStepCounts.TryGetValue(tileKey, out int count);
        count++;
        _tileStepCounts[tileKey] = count;

        var dm = DataManager.Instance;
        int maxSteps = dm != null ? dm.stepTileStepsToCollapse : 3;

        var pv = GameModeManager.Instance?.photonView;
        if (pv == null) return;

        if (count >= maxSteps)
        {
            pv.RPC(nameof(GameModeManager.RPC_StepTileCollapse), RpcTarget.All, x, z);
        }
        else
        {
            pv.RPC(nameof(GameModeManager.RPC_StepTileDarken), RpcTarget.All, x, z, count, maxSteps);
        }
    }

    public void DarkenStepTile(int x, int z, int stepCount, int maxSteps)
    {
        if (x < 0 || x >= _width || z < 0 || z >= _height) return;
        if (_tiles[x, z] == null) return;

        int tileKey = x * 10000 + z;

        // [X3] RPC에 실려 온 마모 카운트를 전 클라이언트가 자기 dict에도 기록한다.
        // 권위(마모 판정)는 마스터 단독이지만 근거 상태를 전 클라에 복제해 두면
        // (1) 비마스터도 IsPositionDangerous가 실제 마모를 반영하고
        // (2) 마스터 교체 시 새 마스터가 마지막 RPC까지의 마모를 그대로 이어받는다.
        _tileStepCounts[tileKey] = stepCount;

        Renderer rend = _tiles[x, z].GetComponentInChildren<Renderer>();
        if (rend == null || rend.sharedMaterial == null) return;

        // 색은 sharedMaterial로 읽고(인스턴스 복제 없음) MaterialPropertyBlock으로 칠한다 → 밟힌
        // Push 타일마다 머티리얼 사본이 생겨 배칭이 깨지던 문제를 막는다. (K1 — FallingTile G3와 동일)
        int colorPropId = rend.sharedMaterial.HasProperty(BaseColorId) ? BaseColorId : ColorId;
        if (!rend.sharedMaterial.HasProperty(colorPropId)) return;

        if (!_tileOriginalColors.TryGetValue(tileKey, out Color original))
        {
            original = rend.sharedMaterial.GetColor(colorPropId);
            _tileOriginalColors[tileKey] = original;
        }

        float t = (float)stepCount / maxSteps;
        Color danger = new Color(original.r * 0.3f, original.g * 0.15f, original.b * 0.1f);

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(_mpb);
        _mpb.SetColor(colorPropId, Color.Lerp(original, danger, t));
        rend.SetPropertyBlock(_mpb);
    }

    public void CollapseStepTile(int x, int z)
    {
        if (x < 0 || x >= _width || z < 0 || z >= _height) return;
        if (_tiles[x, z] == null) return;

        var dm = DataManager.Instance;
        float warn = dm != null ? dm.stepTileWarningDuration : 1.5f;
        float delay = dm != null ? dm.stepTileCollapseDelay : 2f;

        var ft = _tiles[x, z].GetComponent<FallingTile>();
        if (ft == null) ft = _tiles[x, z].AddComponent<FallingTile>();

        ft.GridX = x; ft.GridZ = z;
        ft.StartFall(warn, fallDuration, fallDistance, Mathf.Max(0f, delay - warn));
        _tiles[x, z] = null;
    }

    private void StartIdleShakeOnRing(int ring)
    {
        for (int x = 0; x < _width; x++)
        {
            for (int z = 0; z < _height; z++)
            {
                if (GetRing(x, z) == ring && _tiles[x, z] != null)
                {
                    var ft = _tiles[x, z].GetComponent<FallingTile>();
                    if (ft == null) ft = _tiles[x, z].AddComponent<FallingTile>();
                    ft.StartIdleShake();
                }
            }
        }
    }

    private void CollapseRingAnimated(int ring)
    {
        float delay = 0f;
        for (int x = 0; x < _width; x++)
        {
            for (int z = 0; z < _height; z++)
            {
                if (GetRing(x, z) == ring && _tiles[x, z] != null)
                {
                    var ft = _tiles[x, z].GetComponent<FallingTile>();
                    if (ft == null) ft = _tiles[x, z].AddComponent<FallingTile>();
                    ft.GridX = x; ft.GridZ = z;
                    ft.StartFall(warningDuration, fallDuration, fallDistance, delay);
                    _tiles[x, z] = null;
                    if (tileDelay > 0f) delay += tileDelay;
                }
            }
        }
    }

    public bool IsTileActive(int x, int z)
    {
        if (x < 0 || x >= _width || z < 0 || z >= _height) return false;
        return _tiles[x, z] != null && _tiles[x, z].activeSelf;
    }

    /// <summary>
    /// 월드 좌표가 "위험한 타일" 위인지 판정.
    /// 위험 = 이미 무너졌거나 / 흔들리는 중이거나 / 곧 무너질 예정인 링에 속함.
    /// AI는 이 좌표를 목적지로 잡지 않아야 함.
    /// </summary>
    public bool IsPositionDangerous(Vector3 worldPos)
    {
        if (_stepX == 0f || _stepZ == 0f) return false;

        int x = Mathf.RoundToInt((worldPos.x - _gridOrigin.x) / _stepX);
        int z = Mathf.RoundToInt((worldPos.z - _gridOrigin.z) / _stepZ);

        if (x < 0 || x >= _width || z < 0 || z >= _height) return true;

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            if (_tiles[x, z] == null) return true;
            int tileKey = x * 10000 + z;
            if (_tileStepCounts.TryGetValue(tileKey, out int count))
            {
                var dm = DataManager.Instance;
                int maxSteps = dm != null ? dm.stepTileStepsToCollapse : 3;
                if (count >= maxSteps - 1) return true;
            }
            return false;
        }

        int ring = GetRing(x, z);
        return ring <= _lastShakenRing;
    }

    /// <summary>
    /// 타일이 실제로 붕괴(carve)된 시점에 FallingTile이 호출. 해당 칸을 '허공'으로 표시한다.
    /// 주의: _tiles[x,z]=null은 붕괴 '예약' 시점에 설정되지만 타일은 경고 흔들림 동안
    ///       물리적으로 남아 있으므로, 진짜 사라진 시점(carve)을 따로 기록해야 오탐이 없다.
    /// </summary>
    public void MarkCellCollapsed(int x, int z)
    {
        if (x < 0 || x >= _width || z < 0 || z >= _height) return;
        _collapsedCells.Add(x * 10000 + z);
    }

    /// <summary>
    /// 월드 좌표 아래에 실제 발판이 없는지(=허공) 판정.
    /// 그리드 밖이거나 이미 붕괴 완료(carve)된 칸이면 true.
    /// NavMesh가 잔존하거나 타일 밖으로 베이크돼 AI가 허공에 떠 있는 상황을 잡는 데 쓴다.
    /// </summary>
    public bool IsOverVoid(Vector3 worldPos)
    {
        if (_stepX == 0f || _stepZ == 0f) return false;

        int x = Mathf.RoundToInt((worldPos.x - _gridOrigin.x) / _stepX);
        int z = Mathf.RoundToInt((worldPos.z - _gridOrigin.z) / _stepZ);

        if (x < 0 || x >= _width || z < 0 || z >= _height) return true;
        return _collapsedCells.Contains(x * 10000 + z);
    }

    /// <param name="avoidDangerous">true면 물리적으로 남아 있어도 곧 붕괴할(IsPositionDangerous)
    /// 타일은 후보에서 제외한다. Push 모드 도피처럼 '진짜 안전한' 칸이 필요할 때 사용.
    /// 허공 탈출처럼 일단 발판 있는 칸이면 되는 경우엔 false(기본).</param>
    public bool FindNearestSafeTile(Vector3 worldPos, out Vector3 safePos, bool avoidDangerous = false)
    {
        safePos = Vector3.zero;
        if (_stepX == 0f || _stepZ == 0f) return false;

        int cx = Mathf.Clamp(Mathf.RoundToInt((worldPos.x - _gridOrigin.x) / _stepX), 0, _width - 1);
        int cz = Mathf.Clamp(Mathf.RoundToInt((worldPos.z - _gridOrigin.z) / _stepZ), 0, _height - 1);

        int maxRadius = Mathf.Max(_width, _height);
        for (int r = 1; r <= maxRadius; r++)
        {
            float bestDist = float.MaxValue;
            bool found = false;

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;

                    int tx = cx + dx;
                    int tz = cz + dz;
                    if (tx < 0 || tx >= _width || tz < 0 || tz >= _height) continue;
                    if (_tiles[tx, tz] == null) continue;

                    Vector3 tilePos = _gridOrigin + new Vector3(tx * _stepX, 0f, tz * _stepZ);

                    // 발판은 남아 있어도 마모가 한계 직전(곧 붕괴)인 타일은 '안전'이 아니다.
                    // 이 검사가 없으면 봇들이 닳은 타일로 우르르 도피→서로의 step 마모를 가속해
                    // 한곳에 모여 동시에 무너지는 현상이 생긴다(IsPositionDangerous와 기준 통일).
                    if (avoidDangerous && IsPositionDangerous(tilePos)) continue;

                    float dist = (tilePos - worldPos).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        safePos = tilePos;
                        found = true;
                    }
                }
            }
            if (found) return true;
        }
        return false;
    }

    public int CountSafeTiles()
    {
        int count = 0;
        for (int x = 0; x < _width; x++)
            for (int z = 0; z < _height; z++)
                if (_tiles[x, z] != null) count++;
        return count;
    }

    /// <summary>
    /// NavMeshPath의 코너(경유 지점)들 중 위험 구간을 지나는지 검사.
    /// NavMeshObstacle carving이 지연되는 동안의 이중 안전장치.
    /// </summary>
    public bool IsPathDangerous(Vector3[] corners, int count)
    {
        if (_stepX == 0f || _stepZ == 0f) return false;
        for (int i = 0; i < count; i++)
        {
            if (IsPositionDangerous(corners[i])) return true;
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
        if (_width == 0 || _height == 0 || _stepX == 0f || _stepZ == 0f) return false;

        int margin = _lastShakenRing + 1;
        if (margin * 2 >= _width || margin * 2 >= _height) return false;

        min = new Vector3(
            _gridOrigin.x + margin * _stepX,
            _gridOrigin.y,
            _gridOrigin.z + margin * _stepZ
        );
        max = new Vector3(
            _gridOrigin.x + (_width - 1 - margin) * _stepX,
            _gridOrigin.y,
            _gridOrigin.z + (_height - 1 - margin) * _stepZ
        );
        return true;
    }
}
