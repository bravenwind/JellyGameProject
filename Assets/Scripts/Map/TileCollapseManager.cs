using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        float elapsed = GetSyncedElapsed();
        if (elapsed < 0f) return;

        // 다음 차례인 링을 idle shake로 진입 — Fall 시점보다 ringInterval 만큼 일찍
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
        bool anyCollapsed = false;
        for (int x = 0; x < _width; x++)
        {
            for (int z = 0; z < _height; z++)
            {
                if (GetRing(x, z) == ring && _tiles[x, z] != null)
                {
                    var ft = _tiles[x, z].GetComponent<FallingTile>();
                    if (ft == null) ft = _tiles[x, z].AddComponent<FallingTile>();
                    ft.StartFall(warningDuration, fallDuration, fallDistance, delay);
                    _tiles[x, z] = null;
                    if (tileDelay > 0f) delay += tileDelay;
                    anyCollapsed = true;
                }
            }
        }

        if (anyCollapsed)
            StartCoroutine(RebakeNavMeshAfter(warningDuration + fallDuration + delay));
    }

    private IEnumerator RebakeNavMeshAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gridParent == null) yield break;
        Component surface = gridParent.GetComponent("NavMeshSurface");
        if (surface != null)
            surface.SendMessage("BuildNavMesh", SendMessageOptions.DontRequireReceiver);
    }

    public bool IsTileActive(int x, int z)
    {
        if (x < 0 || x >= _width || z < 0 || z >= _height) return false;
        return _tiles[x, z] != null && _tiles[x, z].activeSelf;
    }
}
