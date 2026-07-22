// ============================================================
// NetworkJellyManager.cs
// ============================================================
// 역할: 멀티플레이 환경에서 젤리 스폰/삭제를 관리
//
// [핵심 규칙]
//   - 젤리 생성: MasterClient(방장)만 담당 (PhotonNetwork.InstantiateRoomObject = 룸 오브젝트) [S9]
//     → "누가 스폰했는지"가 달라지면 중복/누락이 생김. 룸 오브젝트라 마스터 이탈 시에도
//       파괴되지 않고 새 마스터가 소유권을 이어받는다(젤리 일시 증발 방지).
//   - 젤리 흡수/삭제: 먹은 클라가 마스터에 '흡수 요청' → 마스터가 선착 1명 판정 후 [V7]
//     보상 확정(RPC_ConfirmEat) + PhotonNetwork.Destroy (이중 흡수 방지 + 삭제 권한 통일)
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

    // 스폰 루틴 중복 실행 방지 (MasterClient 교체 시 이중 시작 차단).
    // [V6] bool 플래그 대신 코루틴 핸들 자체를 진실(source of truth)로 삼는다 —
    // 플래그는 코루틴이 외부 요인(오브젝트 비활성화 등)으로 죽어도 true로 남아
    // 마스터 교체 시 재시작이 영구 차단됐다. 핸들은 OnDisable에서 함께 비운다.
    private Coroutine _spawnRoutine;

    // [V7] 이중 흡수 방지: 이미 흡수 판정이 확정된 젤리 ViewID (마스터만 사용).
    private readonly HashSet<int> _claimedJellies = new HashSet<int>();

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
            StartSpawnRoutineIfNeeded();
            Debug.Log("[JellyManager] MasterClient: 젤리 스폰 시작");
        }
    }

    /// <summary>스폰 루틴이 안 돌고 있을 때만 시작한다(중복 시작 차단의 단일 진입점). [V6]</summary>
    private void StartSpawnRoutineIfNeeded()
    {
        if (_spawnRoutine != null) return;
        _spawnRoutine = StartCoroutine(SpawnRoutine());
    }

    public override void OnDisable()
    {
        base.OnDisable();
        // 비활성화 시 유니티가 코루틴을 끊는다 — 핸들도 함께 비워
        // 재활성/마스터 교체 시 스폰이 다시 시작될 수 있게 한다. [V6]
        _spawnRoutine = null;
    }

    // ─────────────────────────────────────────────────────────
    // 스폰 루틴 (MasterClient 전용)
    // ─────────────────────────────────────────────────────────

    private IEnumerator SpawnRoutine()
    {
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

            // [V6] 게임이 끝났으면(GameOver/Result) 더 스폰하지 않는다.
            // 결과 전환 준비 중에 젤리가 계속 생기면 파괴 이벤트/씬 전환과 경합한다.
            // (시작 전 카운트다운 동안엔 초기 배치분으로 충분하므로 보충도 쉬어 간다)
            if (GameState.Phase != GamePhase.Playing) continue;

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

        // [S9] 룸 오브젝트로 생성 → 마스터가 방을 나가도 파괴되지 않고 새 마스터에게 소유권 이전.
        // (기존 PhotonNetwork.Instantiate는 CleanupCacheOnLeave 기본값 true 때문에 마스터 이탈 시
        //  그 마스터가 만든 젤리가 전부 파괴됐다가 새 마스터가 재보충 → '젤리 일시 증발' 발생)
        GameObject jelly = PhotonNetwork.InstantiateRoomObject(prefabFolder + prefabName, pos, Quaternion.identity);
        PlaceJellyOnNavMesh(jelly, pos);
        _spawnedJellies.Add(jelly);
    }

    /// <summary>
    /// 스폰한 젤리의 NavMeshAgent를 NavMesh에 확실히 안착시킨다.
    ///
    /// 주의: 활성 NavMeshAgent는 transform.position 직접 대입으로 옮기면 안 된다. 그러면 agent의
    /// 내부 위치와 transform이 어긋나 NavMesh 밖으로 떨어지고, 그 젤리는 '바닥에 박힌 채 전혀
    /// 움직이지 않는' 상태가 된다(WanderingAI는 isOnNavMesh가 false면 이동을 멈춘다). 반드시
    /// Warp을 써야 한다 — Warp은 agent를 navMeshPos(방금 SamplePosition으로 확보한 유효 지점)에
    /// 올바로 안착시키고, 이후 agent가 baseOffset(예: 1.84)만큼 transform을 들어올려 바닥에 박히지
    /// 않게 한다(이 값이 동기화돼 원격 클라에서도 박히지 않음).
    ///
    /// 원격 사본/중력 젤리는 agent가 비활성(JellyColliderAbsorb)이므로 건너뛴다(원격은 동기화 위치를
    /// 따르고, 중력 젤리는 물리로 바닥에 안착).
    /// </summary>
    private static void PlaceJellyOnNavMesh(GameObject jelly, Vector3 navMeshPos)
    {
        if (jelly == null) return;
        var agent = jelly.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.enabled)
            agent.Warp(navMeshPos);
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

        // [S9] 룸 오브젝트로 생성 (마스터 이탈 시 소유권 이전, 파괴 방지)
        GameObject jelly = PhotonNetwork.InstantiateRoomObject(prefabFolder + prefabName, position, Quaternion.identity);
        PlaceJellyOnNavMesh(jelly, position);
        _spawnedJellies.Add(jelly);
    }

    // ─────────────────────────────────────────────────────────
    // 젤리 흡수 (마스터 중재 — 이중 흡수 방지 + 삭제 권한 통일) [V7]
    // ─────────────────────────────────────────────────────────
    //
    // [설계 근거]
    //   - 삭제 권한: PhotonNetwork.Destroy는 소유자/마스터만 호출 가능한데 젤리는 룸(마스터)
    //     소유이므로, 비마스터가 먹었어도 마스터에게 요청해야 한다 → 삭제 트리거 메시지는 필수.
    //   - 이중 흡수(double-eat): 예전엔 보상(성장/색/점수)을 각 클라가 로컬에서 즉시 지급해,
    //     두 엔티티가 같은 젤리를 같은 프레임에 먹으면 둘 다 성장했다. (REVIEW_NOTES V7/L1)
    //   → "요청 → 마스터 선착 1명 판정 → 승자에게만 보상 확정 + 젤리 파괴"로 통일한다.
    //     (플레이어↔플레이어 흡수의 RPC_RequestAbsorbValidation과 동일한 권위 패턴)

    /// <summary>
    /// 젤리를 먹었을 때(흡수 애니 완료 시) 호출. 마스터에게 흡수 판정을 요청한다.
    /// jellyViewID = 먹힌 젤리, eaterViewID = 먹은 플레이어/봇의 PhotonView.
    /// </summary>
    public void RequestEatJelly(int jellyViewID, int eaterViewID)
    {
        photonView.RPC(nameof(RPC_RequestEatJelly), RpcTarget.MasterClient, jellyViewID, eaterViewID);
    }

    /// <summary>[RPC · 마스터] 선착 1명만 승자로 판정 → 승자에게 보상 확정 + 젤리 파괴.</summary>
    [PunRPC]
    private void RPC_RequestEatJelly(int jellyViewID, int eaterViewID)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 이미 다른 요청이 이 젤리를 선점했으면(이중 흡수 시도) 무시 → 후발 요청자는 보상 없음.
        if (_claimedJellies.Contains(jellyViewID)) return;

        PhotonView jellyView = PhotonView.Find(jellyViewID);
        if (jellyView == null) return;   // 이미 파괴됨(늦은 요청)

        _claimedJellies.Add(jellyViewID);

        // 젤리 색(보상 정보)을 읽어 승자에게 실어 보낸다.
        int colorType = 0;
        var jellyObj = jellyView.GetComponent<JellyObject>();
        if (jellyObj != null) colorType = (int)jellyObj.jellyType;

        // 승자 확정을 전 클라에 보내되, 그 엔티티의 소유자만 실제 보상을 적용한다.
        photonView.RPC(nameof(RPC_ConfirmEat), RpcTarget.All, eaterViewID, colorType);

        // 젤리 실제 파괴(룸 오브젝트는 마스터만 파괴 가능 — 규칙 충족).
        _spawnedJellies.Remove(jellyView.gameObject);
        PhotonNetwork.Destroy(jellyView.gameObject);
    }

    /// <summary>[RPC] 흡수 승자의 소유자 클라에서만 보상(성장/색/점수)을 적용한다.</summary>
    [PunRPC]
    private void RPC_ConfirmEat(int eaterViewID, int colorType)
    {
        PhotonView eaterView = PhotonView.Find(eaterViewID);
        if (eaterView == null || !eaterView.IsMine) return;   // 그 엔티티의 소유자만 보상

        // AbsorbColor → OnJellyScored(점수)/OnJellyEaten(색+성장) 기존 보상 경로를 그대로 탄다.
        PlayerAbsorber absorber = eaterView.GetComponentInChildren<PlayerAbsorber>();
        absorber?.AbsorbColor((JellyColorType)colorType);
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
            StartSpawnRoutineIfNeeded();
        }
    }
}
