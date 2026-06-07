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

    private float _stepProcessTimer;
    private const float STEP_PROCESS_INTERVAL = 0.15f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }

        if (gridParent == null) gridParent = transform;
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

    private void Update()
    {
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

        _stepProcessTimer += Time.deltaTime;
        if (_stepProcessTimer < STEP_PROCESS_INTERVAL) return;
        float dt = _stepProcessTimer;
        _stepProcessTimer = 0f;

        foreach (var player in EntityRegistry.Players)
        {
            if (player == null) continue;
            if (player.photonView.Owner?.CustomProperties != null &&
                player.photonView.Owner.CustomProperties.TryGetValue("Eliminated", out object e) &&
                e is bool b && b) continue;
            TryStepAt(player.transform.position, player.photonView.ViewID, dt);
        }

        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || bot.IsEliminated) continue;
            TryStepAt(bot.transform.position, bot.photonView.ViewID, dt);
        }
    }

    private void TryStepAt(Vector3 worldPos, int entityID, float dt)
    {
        int x = Mathf.RoundToInt((worldPos.x - _gridOrigin.x) / _stepX);
        int z = Mathf.RoundToInt((worldPos.z - _gridOrigin.z) / _stepZ);

        if (x < 0 || x >= _width || z < 0 || z >= _height) return;
        if (_tiles[x, z] == null) return;

        int tileKey = x * 10000 + z;

        // 새 타일로 이동: 해당 타일을 1회 마모시키고 체류 타이머 초기화
        if (!_entityCurrentTile.TryGetValue(entityID, out int lastTile) || lastTile != tileKey)
        {
            _entityCurrentTile[entityID] = tileKey;
            _entityDwellTime[entityID] = 0f;
            WearTile(x, z, tileKey);
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
        Renderer rend = _tiles[x, z].GetComponentInChildren<Renderer>();
        if (rend == null) return;

        if (!_tileOriginalColors.ContainsKey(tileKey))
            _tileOriginalColors[tileKey] = rend.material.color;

        float t = (float)stepCount / maxSteps;
        Color original = _tileOriginalColors[tileKey];
        Color danger = new Color(original.r * 0.3f, original.g * 0.15f, original.b * 0.1f);
        rend.material.color = Color.Lerp(original, danger, t);
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

    public bool FindNearestSafeTile(Vector3 worldPos, out Vector3 safePos)
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
