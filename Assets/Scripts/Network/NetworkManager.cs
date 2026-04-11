// ============================================================
// NetworkManager.cs
// ============================================================
// 역할: Photon 서버 연결 + 로비 + 방 생성/입장 관리
//
// [PUN2 흐름]
//   1. ConnectUsingSettings() → Photon 서버에 접속
//   2. OnConnectedToMaster() 콜백 → 로비 입장
//   3. JoinOrCreateRoom() → 방 생성 또는 기존 방 입장
//   4. OnJoinedRoom() 콜백 → 플레이어 프리팹 생성 후 게임 시작
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;           // PhotonNetwork, PhotonView 등 핵심 클래스
using Photon.Realtime;      // IConnectionCallbacks, IMatchmakingCallbacks 등 콜백 인터페이스

public class NetworkManager : MonoBehaviourPunCallbacks
// MonoBehaviourPunCallbacks = MonoBehaviour + Photon 콜백을 자동으로 받을 수 있는 베이스 클래스
{
    // ─────────────────────────────────────────────────────────
    // 인스펙터 설정
    // ─────────────────────────────────────────────────────────

    [Header("플레이어 설정")]
    [Tooltip("Resources 폴더 안에 있는 플레이어 프리팹 이름 (PhotonView 컴포넌트 필수!)")]
    public string playerPrefabName = "NetworkPlayer";

    [Tooltip("플레이어가 스폰될 위치들 (비워두면 'SpawnPoint' 태그로 자동 탐색)")]
    public Transform[] spawnPoints;

    [Tooltip("스폰 포인트 자동 탐색 태그")]
    public string spawnPointTag = "SpawnPoint";

    [Header("AI 봇 설정")]
    [Tooltip("Resources 폴더 안에 있는 AI 봇 프리팹 이름")]
    public string botPrefabName = "AIBot";

    [Tooltip("방 안에 항상 유지할 AI 봇 수")]
    public int botCount = 2;

    [Header("방 설정")]
    [Tooltip("방 최대 인원 (AI 포함)")]
    public byte maxPlayersPerRoom = 4;

    [Tooltip("게임 씬 이름")]
    public string gameSceneName = "Game_io";

    [Header("스폰 겹침 방지")]
    [Tooltip("이미 사용된 스폰포인트와의 최소 거리")]
    public float minSpawnDistance = 3f;

    // 이번 게임에서 이미 사용된 스폰 위치들 (플레이어 + 봇 공용)
    private readonly List<Vector3> _usedSpawnPositions = new List<Vector3>();

    // ─────────────────────────────────────────────────────────
    // 싱글톤
    // ─────────────────────────────────────────────────────────
    public static NetworkManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬 전환해도 유지

            // MasterClient가 LoadLevel() 하면 다른 클라이언트도 자동으로 같은 씬으로 이동
            PhotonNetwork.AutomaticallySyncScene = true;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 연결 시작
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 타이틀/로비 UI에서 "게임 시작" 버튼을 누르면 호출
    /// </summary>
    public void StartConnect(string playerName)
    {
        // 닉네임 설정 (다른 플레이어 화면에서도 보임)
        PhotonNetwork.NickName = string.IsNullOrEmpty(playerName) ? "Jelly" : playerName;

        // PhotonServerSettings(Assets/Photon/PhotonUnityNetworking/Resources)에서
        // App ID와 서버 정보를 읽어서 자동으로 접속
        PhotonNetwork.ConnectUsingSettings();

        Debug.Log("[Network] Photon 서버에 연결 중...");
    }

    // ─────────────────────────────────────────────────────────
    // Photon 콜백들 (자동으로 호출됨 - 건드리지 않아도 됨)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// [자동 콜백] 서버 접속 성공 시 호출
    /// 여기서 바로 로비로 들어감
    /// </summary>
    public override void OnConnectedToMaster()
    {
        Debug.Log("[Network] 마스터 서버 접속 성공! 로비 입장 중...");

        // 로비 입장 (방 목록을 받기 위해 필요)
        PhotonNetwork.JoinLobby();
    }

    /// <summary>
    /// [자동 콜백] 로비 입장 성공 시 호출
    /// </summary>
    public override void OnJoinedLobby()
    {
        Debug.Log("[Network] 로비 입장 완료! 방 탐색 중...");
        JoinOrCreateRoom();
    }

    /// <summary>
    /// 빈 방이 있으면 입장, 없으면 새 방 생성
    /// </summary>
    private void JoinOrCreateRoom()
    {
        // RoomOptions: 방의 속성 설정
        RoomOptions options = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom,
            IsVisible = true,   // 로비에서 보이게
            IsOpen = true       // 입장 가능하게
        };

        // 랜덤 방 입장 (빈 방 없으면 자동으로 새 방 생성됨)
        PhotonNetwork.JoinRandomOrCreateRoom(roomOptions: options);

        Debug.Log("[Network] 방 입장/생성 시도 중...");
    }

    /// <summary>
    /// [자동 콜백] 방 입장 성공 시 호출 → 게임 씬 로드
    /// MasterClient(방장)만 씬을 바꿈 → 나머지 클라이언트는 자동으로 따라옴
    /// </summary>
    public override void OnJoinedRoom()
    {
        Debug.Log($"[Network] 방 입장 완료! 현재 인원: {PhotonNetwork.CurrentRoom.PlayerCount}");

        // PhotonNetwork.AutomaticallySyncScene = true 설정 시,
        // MasterClient가 씬을 바꾸면 다른 클라이언트도 자동으로 같은 씬으로 이동
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LoadLevel(gameSceneName);
        }
    }

    /// <summary>
    /// [자동 콜백] 방 입장 실패 시 (방이 꽉 찼거나 이미 사라진 경우)
    /// </summary>
    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Network] 랜덤 방 입장 실패 ({returnCode}): {message}");
        // 실패해도 JoinRandomOrCreateRoom을 썼기 때문에 자동으로 새 방이 만들어짐
    }

    /// <summary>
    /// [자동 콜백] 접속 끊김
    /// </summary>
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Network] 접속 끊김: {cause}");
    }

    // ─────────────────────────────────────────────────────────
    // 게임 씬 진입 후: 플레이어 + AI 봇 생성
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 씬에서 유효한 스폰 포인트 목록 반환
    /// 인스펙터에 지정된 것이 없으면 태그로 자동 탐색
    /// </summary>
    private Transform[] GetValidSpawnPoints()
    {
        // 인스펙터에 직접 지정된 경우
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            var valid = System.Array.FindAll(spawnPoints, p => p != null);
            Debug.Log($"[Network] 인스펙터 스폰포인트: 전체 {spawnPoints.Length}개, 유효 {valid.Length}개");
            if (valid.Length > 0) return valid;
        }

        // 태그로 씬에서 자동 탐색
        Debug.Log($"[Network] 태그 '{spawnPointTag}'로 스폰포인트 탐색 시작...");
        GameObject[] tagged = GameObject.FindGameObjectsWithTag(spawnPointTag);
        Debug.Log($"[Network] 태그 탐색 결과: {tagged.Length}개");
        if (tagged.Length > 0)
        {
            Transform[] result = new Transform[tagged.Length];
            for (int i = 0; i < tagged.Length; i++)
            {
                result[i] = tagged[i].transform;
                Debug.Log($"[Network] SpawnPoint[{i}]: {tagged[i].name} 위치={tagged[i].transform.position}");
            }
            spawnPoints = result; // 인스펙터에서 확인 가능하도록 저장
            return result;
        }

        Debug.LogWarning("[Network] 스폰포인트를 찾지 못했습니다! 원점(0,0,0)에 스폰됩니다.");
        return new Transform[0];
    }

    /// <summary>
    /// 게임 씬이 로드된 후 GameModeManager에서 호출
    /// → 로컬 플레이어 프리팹 생성
    /// </summary>
    public void SpawnLocalPlayer()
    {
        // 스폰 위치 결정 (없으면 원점)
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        var validPoints = GetValidSpawnPoints();
        if (validPoints.Length > 0)
        {
            int idx = PickNonOverlappingSpawnIndex(validPoints);
            spawnPos = validPoints[idx].position;
            spawnRot = validPoints[idx].rotation;
        }

        _usedSpawnPositions.Add(spawnPos);

        // PhotonNetwork.Instantiate: "Resources/playerPrefabName" 프리팹을 네트워크 전체에 생성
        // → 다른 클라이언트 화면에도 자동으로 생성됨
        GameObject player = PhotonNetwork.Instantiate(
            playerPrefabName,   // 반드시 Resources 폴더 안에 있어야 함!
            spawnPos,
            spawnRot
        );

        Debug.Log($"[Network] 로컬 플레이어 스폰 완료: {player.name}");
    }

    /// <summary>
    /// MasterClient만 AI 봇을 생성
    /// (방장이 AI를 관리하는 책임을 가짐)
    /// </summary>
    public void SpawnBots()
    {
        Debug.Log($"[Network] SpawnBots 호출됨 - IsMasterClient: {PhotonNetwork.IsMasterClient}, InRoom: {PhotonNetwork.InRoom}, BotCount: {botCount}, BotPrefab: '{botPrefabName}'");

        // IsMasterClient: 이 클라이언트가 방장인지 확인
        // AI 봇은 방장 하나만 관리해야 중복 생성이 없음
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[Network] MasterClient가 아니라 봇 스폰 건너뜀");
            return;
        }

        var validPoints = GetValidSpawnPoints();

        for (int i = 0; i < botCount; i++)
        {
            Vector3 candidate = Vector3.zero;
            if (validPoints.Length > 0)
            {
                int idx = PickNonOverlappingSpawnIndex(validPoints);
                candidate = validPoints[idx].position;
            }
            else
            {
                candidate = PickNonOverlappingRandom();
            }

            // NavMesh 위 가장 가까운 위치로 보정
            UnityEngine.AI.NavMeshHit hit;
            Vector3 spawnPos = UnityEngine.AI.NavMesh.SamplePosition(candidate, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas)
                ? hit.position
                : candidate;

            _usedSpawnPositions.Add(spawnPos);

            Debug.Log($"[Network] AI봇 스폰 위치: {spawnPos} (candidate: {candidate})");
            PhotonNetwork.Instantiate(botPrefabName, spawnPos, Quaternion.identity);
            Debug.Log($"[Network] AI 봇 {i + 1} 스폰 완료");
        }
    }

    // ─────────────────────────────────────────────────────────
    // 스폰 겹침 방지
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// validPoints 중 이미 사용된 위치와 minSpawnDistance 이상 떨어진 인덱스를 반환.
    /// 모든 포인트가 겹치면 가장 먼 포인트를 선택.
    /// </summary>
    private int PickNonOverlappingSpawnIndex(Transform[] points)
    {
        // 셔플된 인덱스 순서로 탐색
        int[] indices = new int[points.Length];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        for (int i = indices.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        int bestIdx = indices[0];
        float bestMinDist = 0f;

        foreach (int idx in indices)
        {
            float closest = ClosestUsedDistance(points[idx].position);
            if (closest >= minSpawnDistance)
                return idx;                 // 충분히 멀면 즉시 반환
            if (closest > bestMinDist)
            {
                bestMinDist = closest;
                bestIdx = idx;              // 가장 먼 후보 기록
            }
        }

        return bestIdx; // 모두 겹치면 그나마 가장 먼 포인트
    }

    /// <summary>
    /// SpawnPoint가 없을 때 랜덤 좌표 중 겹치지 않는 위치 반환.
    /// </summary>
    private Vector3 PickNonOverlappingRandom()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector3 candidate = new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f));
            if (ClosestUsedDistance(candidate) >= minSpawnDistance)
                return candidate;
        }
        return new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f));
    }

    private float ClosestUsedDistance(Vector3 pos)
    {
        float closest = float.MaxValue;
        foreach (var used in _usedSpawnPositions)
        {
            float d = Vector3.Distance(pos, used);
            if (d < closest) closest = d;
        }
        return closest;
    }

    // ─────────────────────────────────────────────────────────
    // 유틸리티
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 방에서 나가기 (게임 오버 / 메인 메뉴로 돌아갈 때)
    /// </summary>
    public void LeaveRoom()
    {
        _usedSpawnPositions.Clear();
        PhotonNetwork.LeaveRoom();
    }
}
