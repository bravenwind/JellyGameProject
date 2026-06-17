// ============================================================
// NetworkJellyManager.cs
// ============================================================
// 역할: 멀티플레이 환경에서 젤리 스폰/삭제를 관리
//
// [핵심 규칙]
//   - 젤리 생성: MasterClient(방장)만 담당
//     → "누가 스폰했는지"가 달라지면 중복/누락이 생김
//   - 젤리 삭제: 흡수한 플레이어 클라이언트가 RPC로 요청
//     → MasterClient가 PhotonNetwork.Destroy()로 실제 삭제
//
// [기존 RandomJellySpawner와의 차이]
//   RandomJellySpawner → 싱글플레이용 (로컬 Instantiate)
//   NetworkJellyManager → 멀티플레이용 (PhotonNetwork.Instantiate)
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;  // Player 타입이 여기 있음

public class NetworkJellyManager : MonoBehaviourPunCallbacks
{
    // ─────────────────────────────────────────────────────────
    // 싱글톤
    // ─────────────────────────────────────────────────────────
    public static NetworkJellyManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────
    // 인스펙터 설정
    // ─────────────────────────────────────────────────────────
    [Header("프리팹 경로")]
    [Tooltip("Resources 폴더 기준 서브폴더 경로. 예: 'Prefabs/' → Resources/Prefabs/ 에서 로드")]
    public string prefabFolder = "Prefabs/";

    [Header("젤리 스폰 설정")]
    [Tooltip("젤리 프리팹 이름들 (확장자 제외, Resources/Prefabs/ 안에 있어야 함)")]
    public string[] jellyPrefabNames;

    [Tooltip("맵에 동시에 존재할 최대 젤리 수")]
    public int maxJellyCount = 100;

    [Tooltip("젤리 스폰 간격 (초)")]
    public float spawnInterval = 2f;

    [Tooltip("초기 스폰 이후, spawnInterval마다 한 번에 보충할 젤리 수")]
    public int spawnPerInterval = 1;

    [Header("스폰 범위")]
    public float spawnRangeX = 30f;
    public float spawnRangeZ = 30f;
    public float spawnHeight = 0.5f;

    // ─────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────

    // 현재 스폰된 젤리 목록 (MasterClient만 관리)
    private List<GameObject> _spawnedJellies = new List<GameObject>();

    // 스폰 루틴 중복 실행 방지 (MasterClient 교체 시 이중 시작 차단)
    private bool _spawnRoutineRunning = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            Debug.Log("[JellyManager] Push 모드 — 젤리 스폰 비활성화");
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(SpawnRoutine());
            Debug.Log("[JellyManager] MasterClient: 젤리 스폰 시작");
        }
    }

    // ─────────────────────────────────────────────────────────
    // 스폰 루틴 (MasterClient 전용)
    // ─────────────────────────────────────────────────────────

    private IEnumerator SpawnRoutine()
    {
        // 중복 실행 방지: MasterClient 교체 시 OnMasterClientSwitched와 겹쳐 두 번 시작되는 것을 차단
        if (_spawnRoutineRunning) yield break;
        _spawnRoutineRunning = true;

        // 처음에 젤리를 빠르게 채움 (배치 스폰).
        // 단, MasterClient가 교체된 경우 새 마스터의 _spawnedJellies는 비어 있지만
        // 씬에는 기존 젤리가 그대로 살아있다. 실제 개수(EntityRegistry)를 기준으로
        // 부족분만 채워야 젤리가 maxJellyCount를 초과해 과다 생성되지 않는다.
        int initialBatch = maxJellyCount / 2;
        for (int i = 0; i < initialBatch; i++)
        {
            if (CurrentJellyCount() >= maxJellyCount) break;
            SpawnJelly();
        }

        // 이후 spawnInterval마다 spawnPerInterval개씩 보충
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);

            // 삭제된 젤리 참조 정리
            _spawnedJellies.RemoveAll(j => j == null);

            int perTick = Mathf.Max(1, spawnPerInterval);
            for (int i = 0; i < perTick; i++)
            {
                if (CurrentJellyCount() >= maxJellyCount) break;
                SpawnJelly();
            }
        }
    }

    /// <summary>
    /// 현재 씬에 살아있는 네트워크 젤리 수.
    /// EntityRegistry는 모든 클라이언트에서 JellyObject.OnEnable/OnDisable로 갱신되므로,
    /// MasterClient가 교체되어 로컬 추적 목록(_spawnedJellies)이 비어 있어도
    /// 실제 젤리 개수를 정확히 파악할 수 있다.
    /// </summary>
    private int CurrentJellyCount()
    {
        return EntityRegistry.Jellies.Count;
    }

    private void SpawnJelly()
    {
        if (jellyPrefabNames == null || jellyPrefabNames.Length == 0) return;

        // 랜덤 젤리 타입 선택
        string prefabName = jellyPrefabNames[Random.Range(0, jellyPrefabNames.Length)];

        // NavMesh 위의 유효한 위치에 스폰
        Vector3 pos;
        if (!TryGetNavMeshSpawnPosition(out pos))
        {
            Debug.LogWarning("[JellyManager] NavMesh 위 스폰 위치를 찾지 못했습니다. 스킵.");
            return;
        }

        GameObject jelly = PhotonNetwork.Instantiate(prefabFolder + prefabName, pos, Quaternion.identity);
        _spawnedJellies.Add(jelly);
    }

    /// <summary>
    /// NavMesh 위의 유효한 랜덤 위치를 찾아 반환
    /// 젤리에 NavMeshAgent가 있으면 NavMesh 위에 스폰해야 에러가 안 남
    /// </summary>
    // NavMesh 실제 Y 높이 (자동 감지)
    private float _navMeshBaseY = float.MinValue;

    /// <summary>
    /// NavMesh 실제 Y 좌표를 한 번만 감지해서 캐싱
    /// SpawnPoint 태그 오브젝트 기준으로 찾거나, 넓은 반경으로 탐색
    /// </summary>
    private float GetNavMeshBaseY()
    {
        if (_navMeshBaseY != float.MinValue) return _navMeshBaseY;

        // SpawnPoint 없으면 넓은 반경으로 탐색
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(Vector3.zero, out hit, 500f, UnityEngine.AI.NavMesh.AllAreas))
        {
            _navMeshBaseY = hit.position.y;
            Debug.Log($"[JellyManager] NavMesh 기준 Y = {_navMeshBaseY} (자동 탐색)");
            return _navMeshBaseY;
        }

        _navMeshBaseY = 0f;
        return _navMeshBaseY;
    }

    private bool TryGetNavMeshSpawnPosition(out Vector3 result)
    {
        float baseY = GetNavMeshBaseY();

        float minX = -spawnRangeX, maxX = spawnRangeX;
        float minZ = -spawnRangeZ, maxZ = spawnRangeZ;

        var collapse = TileCollapseManager.Instance;
        if (collapse != null && collapse.GetSafeBounds(out Vector3 safeMin, out Vector3 safeMax))
        {
            minX = safeMin.x;
            maxX = safeMax.x;
            minZ = safeMin.z;
            maxZ = safeMax.z;
        }

        for (int i = 0; i < 30; i++)
        {
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);
            // 후보점의 수직 오프셋은 반드시 SamplePosition 반경(아래 3f)보다 작아야 한다.
            // SamplePosition의 maxDistance는 3D 유클리드 거리라, 지면 위로 5f 띄운 점을 반경 3f로
            // 스냅하려 하면 5>3이라 평지에서 한 번도 못 잡고 스폰이 통째로 실패한다. (J1)
            // 살짝(1f)만 띄워 바로 아래 navmesh가 반경 안에 들도록 한다.
            Vector3 candidate = new Vector3(x, baseY + 1f, z);

            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            {
                if (collapse != null && collapse.IsPositionDangerous(hit.position))
                    continue;
                result = hit.position;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    // ─────────────────────────────────────────────────────────
    // 외부 소환 (JellySpawnMachine 등에서 특정 위치 지정 시)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// MasterClient만 호출. 특정 위치에 젤리를 네트워크 소환하고 관리 목록에 추가.
    /// JellySpawnMachine에서 사용.
    /// </summary>
    public void SpawnJellyAt(string prefabName, Vector3 position)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        _spawnedJellies.RemoveAll(j => j == null);
        if (CurrentJellyCount() >= maxJellyCount) return;

        GameObject jelly = PhotonNetwork.Instantiate(prefabFolder + prefabName, position, Quaternion.identity);
        _spawnedJellies.Add(jelly);
    }

    // ─────────────────────────────────────────────────────────
    // 젤리 삭제 (누구든 요청 가능, MasterClient가 실제 삭제)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 플레이어가 젤리를 흡수했을 때 호출
    /// → MasterClient에게 삭제 요청 RPC 전송
    /// </summary>
    public void RequestDestroyJelly(GameObject jellyObject)
    {
        PhotonView jellyView = jellyObject.GetComponent<PhotonView>();
        if (jellyView == null) return;

        // MasterClient에게만 RPC 전송 (MasterClient가 Destroy 권한을 가짐)
        photonView.RPC(nameof(RPC_DestroyJelly), RpcTarget.MasterClient, jellyView.ViewID);
    }

    /// <summary>
    /// [RPC] MasterClient에서 젤리를 실제로 삭제
    /// </summary>
    [PunRPC]
    private void RPC_DestroyJelly(int jellyViewID)
    {
        // MasterClient만 실행
        if (!PhotonNetwork.IsMasterClient) return;

        PhotonView jellyView = PhotonView.Find(jellyViewID);
        if (jellyView != null)
        {
            _spawnedJellies.Remove(jellyView.gameObject);
            // PhotonNetwork.Destroy → 모든 클라이언트에서 오브젝트 제거
            PhotonNetwork.Destroy(jellyView.gameObject);
        }
    }

    // ─────────────────────────────────────────────────────────
    // MasterClient 이전 처리
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// [자동 콜백] 방장이 나갔을 때 새 방장이 스폰 루틴 이어받음
    /// </summary>
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // Push 모드는 수집용 젤리를 스폰하지 않는다. Start()에는 이 가드가 있었지만
        // 마스터 교체 콜백에는 빠져 있어, 살아남은 비마스터가 새 마스터가 되면 Push
        // 모드인데도 젤리가 소환되던 버그가 있었다.
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            Debug.Log("[JellyManager] Push 모드 — 마스터 교체 시에도 젤리 스폰 안 함");
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("[JellyManager] 새 MasterClient가 됨 → 스폰 루틴 이어받기");
            StartCoroutine(SpawnRoutine());
        }
    }
}
