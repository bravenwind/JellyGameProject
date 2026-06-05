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

using Photon.Pun;           // PhotonNetwork, PhotonView 등 핵심 클래스
using Photon.Realtime;      // IConnectionCallbacks, IMatchmakingCallbacks 등 콜백 인터페이스
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    [Header("프리팹 경로")]
    [Tooltip("Resources 폴더 기준 서브폴더 경로. 예: 'Prefabs/' → Resources/Prefabs/ 에서 로드")]
    public string prefabFolder = "Prefabs/";

    [Header("방 설정")]
    [Tooltip("방 최소 인원")]
    public int minPlayersPerRoom = 2;

    [Tooltip("최소 인원 달성 후 카운트다운 전 대기 시간 (가짜 매칭 연출용)")]
    public float matchBufferSeconds = 6f;

    [Tooltip("방 최대 인원 (AI 포함)")]
    public int maxPlayersPerRoom = 10;

    [Tooltip("매칭 타이머")]
    public float noPlayerConnectTimeSeconds = 10.0f;

    [Tooltip("게임 씬 이름 (흡수 모드)")]
    public string gameAbsorbModeSceneName = "Game_io_AbsorbMode";

    [Tooltip("게임 씬 이름 (밀치기 모드)")]
    public string gamePushModeSceneName = "Game_io_PushMode";

    [Tooltip("로딩 씬 이름")]
    public string loadingSceneName = "Loading";

    [Header("스폰 겹침 방지")]
    [Tooltip("이미 사용된 스폰포인트와의 최소 거리")]
    public float minSpawnDistance = 3f;

    [Tooltip("가상 스폰포인트 생성 시 탐색 반경")]
    public float virtualSpawnRadius = 30f;

    // 이번 게임의 스폰 슬롯 (물리 SpawnPoint + 부족하면 NavMesh 기반 가상 포인트)
    // 인덱스로 분배: 0..PlayerCount-1 → 플레이어, 그 뒤 → 봇
    private readonly List<Vector3> _spawnSlots = new List<Vector3>();
    private bool _slotsPrepared = false;

    // 이벤트 코드 (다른 RaiseEvent와 안 겹치게)
    public const byte EVENT_COUNTDOWN = 11;
    public const byte EVENT_GAME_START = 12;
    public const byte EVENT_PLAYER_COUNT = 13;

    private bool _isCountingDown = false;
    private bool _wantsToJoin = false;
    private int _reconnectAttempts = 0;
    private const int MaxReconnectAttempts = 3;

    public const string ROOM_PROP_GAME_MODE = "GM";

    public static GameModeType SelectedGameMode { get; set; } = GameModeType.Absorb;

    // ─────────────────────────────────────────────────────────
    // 싱글톤
    // ─────────────────────────────────────────────────────────
    public static NetworkManager Instance { get; private set; }

    private void Awake()
    {
        QualitySettings.vSyncCount = 1;

        // 모바일 환경이나 일반적인 PC 환경이라면 60, 부드러운 액션이 필요하다면 120이나 144로 설정합니다.
        Application.targetFrameRate = 60;

        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            PhotonNetwork.AutomaticallySyncScene = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _spawnSlots.Clear();
        _slotsPrepared = false;
        spawnPoints = null;
        // 게임 오버/일시정지 상태에서 씬을 넘어가도 시간이 멈추지 않도록 복구
        Time.timeScale = 1f;
    }

    // ─────────────────────────────────────────────────────────
    // 연결 시작
    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// 타이틀/로비 UI에서 "게임 시작" 버튼을 누르면 호출
    /// </summary>
    public void StartConnect(string playerName)
    {
        PhotonNetwork.NickName = playerName;
        _wantsToJoin = true;

        // 1. 아예 서버와 연결되지 않은 상태 (최초 실행 시 PeerCreated, 끊겼을 때 Disconnected 포함)
        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log($"[NetworkManager] '{playerName}' 닉네임으로 서버 연결 시도...");
            PhotonNetwork.ConnectUsingSettings();
        }
        // 2. 서버에는 연결되어 있지만, 아직 방에 들어가지 않은 준비 완료 상태 (로비 대기 등)
        else if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
        {
            Debug.Log("[NetworkManager] 이미 서버에 연결되어 있습니다. 바로 방 접속을 시도합니다.");
            JoinOrCreateRoom();
        }
        // 3. (예외처리) 이미 방에 들어가 있는 경우 씬만 전환
        else if (PhotonNetwork.InRoom)
        {
            Debug.Log("[NetworkManager] 이미 방에 입장해 있습니다. 씬 전환을 시도합니다.");
            if (PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.LoadLevel(gameAbsorbModeSceneName);
            }
        }
        // 4. 통신 중인 상태 (Connecting, Joining 등)
        else
        {
            Debug.LogWarning($"[NetworkManager] 현재 서버 통신 중입니다. 중복 요청을 무시합니다. (상태: {PhotonNetwork.NetworkClientState})");
        }
    }

    // ─────────────────────────────────────────────────────────
    // Photon 콜백들 (자동으로 호출됨 - 건드리지 않아도 됨)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// [자동 콜백] 서버 접속 성공 시 호출
    /// </summary>
    public override void OnConnectedToMaster()
    {
        Debug.Log("마스터 서버 연결 완료!");
        _reconnectAttempts = 0;

        if (_wantsToJoin)
        {
            JoinOrCreateRoom();
        }
    }

    /// <summary>
    /// 빈 방이 있으면 입장, 없으면 새 방 생성
    /// </summary>
    private void JoinOrCreateRoom()
    {
        string modeStr = SelectedGameMode.ToString();

        RoomOptions options = new RoomOptions
        {
            MaxPlayers = (byte)maxPlayersPerRoom,
            IsVisible = true,
            IsOpen = true,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable
            {
                { ROOM_PROP_GAME_MODE, modeStr }
            },
            CustomRoomPropertiesForLobby = new[] { ROOM_PROP_GAME_MODE }
        };

        ExitGames.Client.Photon.Hashtable expectedProps = new ExitGames.Client.Photon.Hashtable
        {
            { ROOM_PROP_GAME_MODE, modeStr }
        };

        PhotonNetwork.JoinRandomOrCreateRoom(
            expectedCustomRoomProperties: expectedProps,
            expectedMaxPlayers: (byte)maxPlayersPerRoom,
            roomOptions: options
        );

        Debug.Log($"[Network] 방 입장/생성 시도 (모드: {modeStr})...");
    }

    /// <summary>
    /// [자동 콜백] 방 입장 성공 시 호출 → 게임 씬 로드
    /// MasterClient(방장)만 씬을 바꿈 → 나머지 클라이언트는 자동으로 따라옴
    /// </summary>
    public override void OnJoinedRoom()
    {
        _wantsToJoin = false;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(ROOM_PROP_GAME_MODE, out object modeObj)
            && System.Enum.TryParse<GameModeType>(modeObj.ToString(), out var mode))
        {
            GameState.CurrentGameMode = mode;
        }
        else
        {
            GameState.CurrentGameMode = SelectedGameMode;
        }

        Debug.Log($"[Network] 방 입장 완료! 모드: {GameState.CurrentGameMode}, 인원: {PhotonNetwork.CurrentRoom.PlayerCount}");
        BroadcastPlayerCount();
        CheckAndStartCountdown();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[Network] {newPlayer.NickName} 입장, 현재 {PhotonNetwork.CurrentRoom.PlayerCount}명");
        BroadcastPlayerCount();
        CheckAndStartCountdown();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        BroadcastPlayerCount();
    }

    private void BroadcastPlayerCount()
    {
        int count = PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;
        PhotonNetwork.RaiseEvent(
            EVENT_PLAYER_COUNT, count,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            ExitGames.Client.Photon.SendOptions.SendReliable
        );
    }

    private void CheckAndStartCountdown()
    {
        if (!PhotonNetwork.IsMasterClient || _isCountingDown) return;
        if (PhotonNetwork.CurrentRoom.PlayerCount >= minPlayersPerRoom)
        {
            _isCountingDown = true;
            StartCoroutine(CountdownCoroutine());
        }
    }

    private IEnumerator CountdownCoroutine()
    {
        // 최소 인원은 채워졌지만, 바로 시작하면 어색하므로 잠깐 더 대기
        yield return new WaitForSeconds(matchBufferSeconds);

        // 카운트다운 시작하면 새 입장 막기
        PhotonNetwork.CurrentRoom.IsOpen = false;

        for (int i = 3; i >= 1; i--)
        {
            PhotonNetwork.RaiseEvent(
                EVENT_COUNTDOWN, i,
                new RaiseEventOptions { Receivers = ReceiverGroup.All },
                ExitGames.Client.Photon.SendOptions.SendReliable
            );
            yield return new WaitForSeconds(1f);
        }

        // "게임 시작!" 신호
        PhotonNetwork.RaiseEvent(
            EVENT_GAME_START, null,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            ExitGames.Client.Photon.SendOptions.SendReliable
        );

        yield return new WaitForSeconds(1.5f); // 텍스트 보여주는 시간

        // 변경
        botCount = maxPlayersPerRoom - PhotonNetwork.CurrentRoom.PlayerCount;

        LoadingSceneController.NextSceneName =
            (GameState.CurrentGameMode == GameModeType.Push)
                ? gamePushModeSceneName
                : gameAbsorbModeSceneName;

        PhotonNetwork.LoadLevel(loadingSceneName);
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
    /// [자동 콜백] 접속 끊김 — 일시적 원인이면 재연결 시도
    /// </summary>
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Network] 접속 끊김: {cause}");

        bool isTransient = cause == DisconnectCause.ClientTimeout
                        || cause == DisconnectCause.ServerTimeout
                        || cause == DisconnectCause.Exception
                        || cause == DisconnectCause.ExceptionOnConnect;

        if (isTransient && _reconnectAttempts < MaxReconnectAttempts)
        {
            _reconnectAttempts++;
            Debug.Log($"[Network] 재연결 시도 {_reconnectAttempts}/{MaxReconnectAttempts}...");
            StartCoroutine(ReconnectCoroutine());
        }
        else
        {
            _reconnectAttempts = 0;
            _isCountingDown = false;
            SceneManager.LoadScene("Main");
        }
    }

    private IEnumerator ReconnectCoroutine()
    {
        yield return new WaitForSeconds(1f);

        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[NetworkManager] 방에서 성공적으로 나갔습니다. 메인 씬으로 돌아갑니다.");
        _isCountingDown = false;
        SceneManager.LoadScene("Main");
    }

    // ─────────────────────────────────────────────────────────
    // 게임 씬 진입 후: 플레이어 + AI 봇 생성
    // ─────────────────────────────────────────────────────────

    private List<Transform> GetValidSpawnPoints()
    {
        List<Transform> resultList = new List<Transform>();

        // 1. 인스펙터에 직접 지정된 경우
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            var valid = System.Array.FindAll(spawnPoints, p => p != null);
            Debug.Log($"[Network] 인스펙터 스폰포인트: 전체 {spawnPoints.Length}개, 유효 {valid.Length}개");
            if (valid.Length > 0)
            {
                resultList.AddRange(valid);
                return resultList;
            }
        }

        // 2. 태그로 씬에서 자동 탐색
        Debug.Log($"[Network] 태그 '{spawnPointTag}'로 스폰포인트 탐색 시작...");
        GameObject[] tagged = GameObject.FindGameObjectsWithTag(spawnPointTag);
        Debug.Log($"[Network] 태그 탐색 결과: {tagged.Length}개");
        if (tagged.Length > 0)
        {
            Transform[] result = new Transform[tagged.Length];
            for (int i = 0; i < tagged.Length; i++)
            {
                result[i] = tagged[i].transform;
                resultList.Add(tagged[i].transform);
                Debug.Log($"[Network] SpawnPoint[{i}]: {tagged[i].name} 위치={tagged[i].transform.position}");
            }
            spawnPoints = result; // 인스펙터에서 확인 가능하도록 저장
            return resultList;
        }

        Debug.LogWarning("[Network] 스폰포인트를 찾지 못했습니다! 기본 리스트(빈 상태)를 반환합니다.");
        return resultList;
    }

    /// <summary>
    /// 스폰 슬롯 사전 준비. SpawnLocalPlayer/SpawnBots 호출 전에 한 번 실행.
    /// 물리 SpawnPoint를 먼저 채우고, 부족하면 NavMesh 기반 가상 포인트로 보충.
    /// 슬롯 인덱스로 분배: 플레이어들이 0..PlayerCount-1, 봇들이 그 뒤.
    /// </summary>
    public void PrepareSpawnSlots()
    {
        if (_slotsPrepared) return;

        _spawnSlots.Clear();

        // [안정성 개선] 런타임 인원 변동으로 인한 슬롯 부족을 원천 차단하기 위해 
        // 방의 최대 인원(maxPlayersPerRoom)만큼 슬롯을 항상 넉넉하게 확보합니다.
        int needed = Mathf.Max(maxPlayersPerRoom, (PhotonNetwork.CurrentRoom?.PlayerCount ?? 1) + botCount);

        // 1. 물리 SpawnPoint 채우기
        var physical = GetValidSpawnPoints();
        foreach (var t in physical)
            if (t != null) _spawnSlots.Add(t.position);

        // 2. 폴백: 물리 SpawnPoint가 없으면 NavMesh 삼각망 정점을 시드로 사용
        // (없는 경우 origin이 Vector3.zero가 되어 가상 포인트도 못 만들고 봇이 원점에 스폰되는 버그 방지)
        if (_spawnSlots.Count == 0)
        {
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            if (tri.vertices != null && tri.vertices.Length > 0)
            {
                Vector3 seed = tri.vertices[Random.Range(0, tri.vertices.Length)];
                _spawnSlots.Add(seed);
                Debug.LogWarning($"[Network] 물리 SpawnPoint 없음 - NavMesh 정점 {seed}을 시드로 사용");
            }
            else
            {
                Debug.LogError("[Network] 물리 SpawnPoint도 NavMesh도 없음! 봇 스폰 실패가 예상됨");
            }
        }

        // 3. 부족하면 NavMesh 기반 가상 포인트로 보충
        Vector3 origin = _spawnSlots.Count > 0 ? _spawnSlots[0] : Vector3.zero;
        int guard = 0;
        while (_spawnSlots.Count < needed && guard++ < needed * 5)
        {
            if (TryGenerateVirtualSpawnPoint(origin, out Vector3 p))
                _spawnSlots.Add(p);
            else
                break;
        }

        _slotsPrepared = true;
        Debug.Log($"[Network] 스폰 슬롯 준비 완료: 물리 {physical.Count}, 가상 {_spawnSlots.Count - physical.Count}, 총 {_spawnSlots.Count}/{needed}");
    }

    private bool TryGenerateVirtualSpawnPoint(Vector3 origin, out Vector3 result)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector2 circle = Random.insideUnitCircle * virtualSpawnRadius;
            Vector3 candidate = origin + new Vector3(circle.x, 0f, circle.y);

            if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out var hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                bool tooClose = false;
                foreach (var existing in _spawnSlots)
                {
                    if (Vector3.Distance(existing, hit.position) < minSpawnDistance) { tooClose = true; break; }
                }
                if (!tooClose) { result = hit.position; return true; }
            }
        }
        result = origin;
        return false;
    }

    private Vector3 GetSlot(int idx)
    {
        if (_spawnSlots.Count == 0) return Vector3.zero;
        // 인덱스가 범위를 벗어나지 않도록 안전하게 제한
        return _spawnSlots[Mathf.Clamp(idx, 0, _spawnSlots.Count - 1)];
    }

    /// <summary>
    /// 로컬 플레이어의 슬롯 인덱스를 PlayerList(ActorNumber 오름차순) 내 위치로 계산.
    /// 모든 클라이언트가 동일한 PlayerList를 공유하므로 각자 고유한 0-based 인덱스를 얻는다.
    /// ActorNumber-1을 직접 인덱스로 쓰면 입/퇴장 반복으로 번호가 비연속이 되어
    /// 슬롯 충돌(봇과 겹침)이나 범위 초과가 발생하므로 반드시 정렬 인덱스를 사용한다.
    /// </summary>
    private int GetLocalPlayerSlotIndex()
    {
        var local = PhotonNetwork.LocalPlayer;
        if (local == null) return 0;

        var sorted = PhotonNetwork.PlayerList.OrderBy(p => p.ActorNumber).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            if (sorted[i].ActorNumber == local.ActorNumber)
                return i;
        }
        return 0;
    }

    /// <summary>
    /// 게임 씬이 로드된 후 GameModeManager에서 호출
    /// → 로컬 플레이어 프리팹 생성. ActorNumber로 슬롯 결정.
    /// </summary>
    public void SpawnLocalPlayer()
    {
        if (!_slotsPrepared) PrepareSpawnSlots();

        // 슬롯 분배: PlayerList 내 정렬된 인덱스를 사용한다.
        // ActorNumber-1을 직접 쓰면 입/퇴장이 반복되어 번호가 비연속(예: 1,3,5)이 됐을 때
        // 슬롯 범위를 벗어나거나 봇 슬롯과 충돌한다.
        int slotIdx = GetLocalPlayerSlotIndex();
        Vector3 spawnPos = GetSlot(slotIdx);

        GameObject player = PhotonNetwork.Instantiate(
            prefabFolder + playerPrefabName,
            spawnPos,
            Quaternion.identity
        );

        Debug.Log($"[Network] 로컬 플레이어 스폰 완료: {player.name} 슬롯[{slotIdx}] 위치={spawnPos}");
    }

    /// <summary>
    /// MasterClient만 AI 봇을 생성. 플레이어들이 사용한 슬롯 다음부터 봇 배치.
    /// </summary>
    public void SpawnBots()
    {
        Debug.Log($"[Network] SpawnBots 호출됨 - IsMasterClient: {PhotonNetwork.IsMasterClient}, InRoom: {PhotonNetwork.InRoom}, BotCount: {botCount}, BotPrefab: '{botPrefabName}'");

        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[Network] MasterClient가 아니라 봇 스폰 건너뜀");
            return;
        }

        if (!_slotsPrepared) PrepareSpawnSlots();

        // 플레이어들이 슬롯 0..PlayerCount-1을 점유하므로 봇은 그 뒤부터 시작한다.
        // (SpawnLocalPlayer가 PlayerList 정렬 인덱스로 슬롯을 잡으므로 정확히 PlayerCount개를 점유.
        //  과거처럼 ActorNumber 최대값을 쓰면 비연속 번호 때문에 슬롯이 건너뛰어져 봇이 멀리 스폰될 수 있다.)
        int botStartIdx = PhotonNetwork.PlayerList.Length;

        // [수정완료] 컴파일 에러를 일으키던 과거의 validPoints 찌꺼기 로직 완전 제거!
        // 깨끗하게 정리된 슬롯 기반 루프로 안전하게 대량 스폰 진행
        for (int i = 0; i < botCount; i++)
        {
            int currentSlotIdx = botStartIdx + i;
            Vector3 spawnPos = GetSlot(currentSlotIdx);

            // 슬롯이 맵 외곽이나 NavMesh 변경 등으로 무효해졌을 경우를 대비해 최종 보정
            if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out var hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                spawnPos = hit.position;

            Debug.Log($"[Network] AI봇 스폰 슬롯[{currentSlotIdx}] 위치={spawnPos}");
            PhotonNetwork.InstantiateRoomObject(prefabFolder + botPrefabName, spawnPos, Quaternion.identity);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 유틸리티
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 방에서 나가기 (게임 오버 / 메인 메뉴로 돌아갈 때)
    /// </summary>
    public void LeaveRoom()
    {
        _wantsToJoin = false;
        _spawnSlots.Clear();
        _slotsPrepared = false;
        PhotonNetwork.LeaveRoom();
    }

    /// <summary>
    /// 메인 메뉴로 복귀. 방 안이면 LeaveRoom → OnLeftRoom → LoadScene("Main"),
    /// 방 밖이면 바로 LoadScene("Main").
    /// </summary>
    public void GoToMainMenu()
    {
        _isCountingDown = false;
        if (PhotonNetwork.InRoom)
            LeaveRoom(); // OnLeftRoom 콜백에서 씬 전환
        else
            SceneManager.LoadScene("Main");
    }
}