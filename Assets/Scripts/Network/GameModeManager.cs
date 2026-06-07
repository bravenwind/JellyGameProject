// ============================================================
// GameModeManager.cs (최적화 버전)
// ============================================================
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class GameModeManager : MonoBehaviourPunCallbacks
{
    public static GameModeManager Instance { get; private set; }

    [Header("전체 게임 시간")]
    public float gameDuration = 180f;
    public float endImpendingTime = 10.0f;

    [Header("UI 연결 — 중앙 카운트다운")]
    public TextMeshProUGUI centerCountdownText;

    [Header("UI 연결 — 전체 게임")]
    public TextMeshProUGUI gameTimerText;

    [Header("UI 연결 — 결과")]
    public GameObject gameResultPanel;
    public TextMeshProUGUI resultTitleText;
    public string gameEndText = "게임 종료!";
    public const string RESULT_SCENE_NAME_ABSORB = "GameResult_AbsorbMode";
    public const string RESULT_SCENE_NAME_PUSH = "GameResult_PushMode";

    [Header("UI 연결 — 순위표")]
    public Transform leaderboardContainer;
    public GameObject leaderboardEntryPrefab;

    private bool _gameRunning = false;

    [SerializeField]
    private float _gameTimer = 0f;
    private static bool _spawned = false;

    private bool _isEndingSequenceStarted = false;

    // 결과 씬 진입 전, 룸 프로퍼티(봇 색상 등) 동기화 완료를 확인하기 위한 토큰 키
    private const string RESULT_SYNC_TOKEN_KEY = "ResultSyncToken";

    // Push 모드 종료 시 마스터가 기록하는 '권위적 생존 플레이어 ActorNumber 목록' 키.
    // 결과 씬은 각 클라이언트의 자기-보고("Eliminated")가 아닌 이 마스터 권위값을 신뢰한다.
    // (탈락 당사자 클라이언트가 자신의 "Eliminated"=true를 자기 화면에서 제때 못 읽어
    //  결과 씬에 자기 자신이 잘못 표시되는 PUN 로컬 캐시 타이밍 버그를 막는다.)
    public const string PUSH_SURVIVOR_ACTORS_KEY = "PushSurvivorActors";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _spawned = false;
    }

    private NetworkPlayerSync _localPlayer;
    private List<LeaderboardEntry> _leaderboardEntries = new List<LeaderboardEntry>();
    private ObjectPool<LeaderboardEntry> _leaderboardPool;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (leaderboardEntryPrefab != null && leaderboardContainer != null)
        {
            var entryComp = leaderboardEntryPrefab.GetComponent<LeaderboardEntry>();
            if (entryComp != null)
                _leaderboardPool = new ObjectPool<LeaderboardEntry>(entryComp, leaderboardContainer, 5);
        }

        Time.timeScale = 1f;
    }

    private void OnDestroy()
    {
        _spawned = false;
        Time.timeScale = 1f;
    }

    private void Start()
    {
        if (PhotonNetwork.InRoom) SpawnAndStartGame();
    }

    public override void OnJoinedRoom()
    {
        SpawnAndStartGame();
    }

    /// <summary>
    /// 룸 커스텀 프로퍼티에 저장된 게임 모드를 GameState에 복원한다.
    /// 모든 클라이언트가 동일한 룸 권위값을 읽으므로 호스트/게스트 간 모드 불일치를 막는다.
    /// </summary>
    private void RestoreGameModeFromRoom()
    {
        if (!PhotonNetwork.InRoom) return;
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                NetworkManager.ROOM_PROP_GAME_MODE, out object modeObj)
            && System.Enum.TryParse<GameModeType>(modeObj.ToString(), out var mode))
        {
            GameState.CurrentGameMode = mode;
            Debug.Log($"[GameMode] 룸 프로퍼티로부터 게임 모드 복원: {mode}");
        }
    }

    private void SpawnAndStartGame()
    {
        // 중복 스폰 방지는 _spawned(씬마다 Awake에서 리셋)로만 한다.
        // 주의: 예전엔 GameState.Phase == Playing 가드가 있었으나, 비동기 씬 로드 직후
        // 마스터의 RPC_StartGame(Phase=Playing)이 이 클라이언트의 SpawnAndStartGame보다
        // 먼저 처리되면 SpawnLocalPlayer가 통째로 스킵되어, 해당 플레이어가 네트워크에
        // 인스턴스화되지 않고 다른 클라이언트에게 보이지 않게 되는 레이스가 있었다. (제거)
        if (_spawned) return;
        _spawned = true;

        // [중요] AutomaticallySyncScene으로 씬 전환 시 PUN이 메시지 큐를 멈추는데,
        // 클라이언트에서 이게 재개되지 않은 채 남으면 플레이어 생성(Instantiate)·타일
        // 붕괴 RPC 등 모든 네트워크 이벤트가 게임 내내 버퍼링되다 결과 씬 전환 때
        // 한꺼번에 처리된다(서로 안 보임 / 타일 desync / "젤리가 게임 끝에 소환").
        // 게임 씬 로드가 끝난 시점(이 Start 시점)이므로 큐를 재개해도 안전하다.
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMessageQueueRunning)
        {
            Debug.LogWarning("[GameMode] 메시지 큐가 멈춰 있어 강제 재개합니다 (씬 동기화 desync 방지).");
            PhotonNetwork.IsMessageQueueRunning = true;
        }

        // 게임 씬 진입 시 DataManager.Awake의 GameState.Reset()이 모드를 Absorb로 되돌리므로,
        // 룸 커스텀 프로퍼티(권위값)로부터 실제 게임 모드를 다시 복원한다.
        // (이 처리가 없으면 빌드로 접속한 클라이언트는 Push 모드여도 좌클릭 공격이 안 된다.)
        RestoreGameModeFromRoom();

        // 1. 가상 포인트 포함 스폰 슬롯 미리 준비 → 2. 로컬 플레이어 → 3. 봇
        NetworkManager.Instance?.PrepareSpawnSlots();
        NetworkManager.Instance?.SpawnLocalPlayer();
        NetworkManager.Instance?.SpawnBots();

        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(RPC_StartGame), RpcTarget.All);
        }
    }

    [PunRPC]
    private void RPC_StartGame()
    {
        StartGameInternal(gameDuration);

        if (PhotonNetwork.IsMasterClient)
        {
            Hashtable props = new Hashtable { { "GameStartTime", PhotonNetwork.ServerTimestamp } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
        Debug.Log($"[GameMode] 게임 시작! 전체시간={gameDuration}s");
    }

    // 💡 중복 제거: 게임 시작 시 공통 초기화 로직 분리
    private void StartGameInternal(float startTime)
    {
        GameState.ResetValues();
        _gameRunning = true;
        _gameTimer = (GameState.CurrentGameMode == GameModeType.Push) ? 0f : startTime;
        GameState.Phase = GamePhase.Playing;

        if (gameResultPanel != null)
            gameResultPanel.SetActive(false);
    }

    private void Update()
    {
        if (!_gameRunning) return;

        if (Time.frameCount % 30 == 0)
            UpdateLeaderboard();

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            _gameTimer += Time.deltaTime;
            UpdateGameTimerUI();

            if (PhotonNetwork.IsMasterClient && Time.frameCount % 60 == 0)
                CheckLastSurvivor();
            return;
        }

        _gameTimer -= Time.deltaTime;
        UpdateGameTimerUI();

        if (_gameTimer <= 3f && !_isEndingSequenceStarted)
        {
            _isEndingSequenceStarted = true;
            StartCoroutine(GameEndingSequenceRoutine());
        }
    }

    private IEnumerator GameEndingSequenceRoutine()
    {
        int count = 3;

        if (centerCountdownText != null)
        {
            centerCountdownText.gameObject.SetActive(true);
        }

        // 3, 2, 1 카운트다운 처리
        while (count > 0)
        {
            if (centerCountdownText != null)
            {
                centerCountdownText.text = count.ToString();
                StartCoroutine(AnimateCenterText(centerCountdownText));
            }

            // 💡 1초씩 대기하되, 타임 슬로우의 영향을 받지 않고 현실 시간(1초)대로 흐르게 무시(Unscaled) 처리
            yield return new WaitForSecondsRealtime(1f);
            count--;
        }

        // 게임 시간 정지 — 이 시점부터 흡수/대쉬 등 전투 행동을 차단한다.
        _gameRunning = false;
        _gameTimer = 0f;
        PlaySFXAudio.Instance?.StopWalking();
        GameState.Phase = GamePhase.Result;

        if (centerCountdownText != null)
        {
            centerCountdownText.text = gameEndText;
            // 게임 종료 텍스트는 사라지지 않고 화면에 유지
            centerCountdownText.transform.localScale = Vector3.one * 1.5f;
            Color c = centerCountdownText.color;
            c.a = 1f;
            centerCountdownText.color = c;
        }

        // 💡 게임 속도가 점점 느려지는 연출 (100% -> 10% 속도로)
        float slowDuration = 1.2f; // 슬로우 모션 지속 시간
        float elapsed = 0f;
        while (elapsed < slowDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 슬로우 모션 중이므로 unscaled 필수
            Time.timeScale = Mathf.Lerp(1f, 0.1f, elapsed / slowDuration);
            yield return null;
        }

        Time.timeScale = 0f; // 완전히 정지

        // 잠시 멈췄다가 최종 결과 도출 및 씬 전환 준비
        yield return new WaitForSecondsRealtime(1.0f);

        // 타임스케일은 0(정지)으로 유지한 채 결과 전환을 시작한다.
        // 여기서 1f로 복구하면 결과 동기화 대기(최대 2초) 동안 플레이어/봇이 다시 움직이는
        // 버그가 발생한다. 씬이 로드되면 NetworkManager.OnSceneLoaded가 timeScale을 1로 복구한다.
        GameWin();
    }

    private IEnumerator AnimateCenterText(TextMeshProUGUI targetText)
    {
        float duration = 0.9f; // 1초가 지나기 전 투명화를 끝냄
        float elapsed = 0f;

        Vector3 startScale = Vector3.one * 0.5f;
        Vector3 targetScale = Vector3.one * 2.5f; // 점점 커지는 형태

        Color startColor = targetText.color;
        startColor.a = 1f; // 시작은 불투명하게

        while (elapsed < duration)
        {
            // 타임 슬로우에 영향받지 않게 unscaledDeltaTime 사용
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;

            // 크기 보간 (점점 커짐)
            targetText.transform.localScale = Vector3.Lerp(startScale, targetScale, t);

            // 색상 보간
            startColor.a = Mathf.Lerp(1f, 0f, t);
            targetText.color = startColor;

            yield return null;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 공통 데이터 헬퍼 (💡 중복 제거의 핵심)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 방의 모든 플레이어와 봇의 (이름, 점수, 크기, 봇여부)를 가져와
    /// 크기 내림차순으로 정렬하여 반환. 점수는 크기에서 자동 산출.
    /// </summary>
    private List<(string name, int score, float scale, bool isBot, Color color)> GetSortedScores()
    {
        var dm = DataManager.Instance;
        var entries = new List<(string name, int score, float scale, bool isBot, Color color)>();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties.TryGetValue("Eliminated", out object elim) && elim is bool b && b)
                continue;
            float scale = player.CustomProperties.TryGetValue("Scale", out object sc) ? (float)sc : dm.startingScale;
            int score = dm.ScoreFromScale(scale);

            Color playerColor = Color.white;
            foreach (var nps in EntityRegistry.Players)
            {
                if (nps != null && nps.photonView.Owner?.ActorNumber == player.ActorNumber)
                {
                    playerColor = nps.DisplayColor;
                    break;
                }
            }

            entries.Add((player.NickName, score, scale, false, playerColor));
        }

        var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
        foreach (var key in roomProps.Keys)
        {
            string keyStr = key.ToString();
            if (keyStr.EndsWith("_Name"))
            {
                string prefix = keyStr.Replace("_Name", "");
                object nameVal = roomProps[keyStr];
                if (nameVal == null) continue;

                if (prefix.StartsWith("Bot") && int.TryParse(prefix.Substring(3), out int vid))
                {
                    bool eliminated = false;
                    foreach (var b in EntityRegistry.Bots)
                    {
                        if (b != null && b.photonView != null && b.photonView.ViewID == vid && b.IsEliminated)
                        {
                            eliminated = true;
                            break;
                        }
                    }
                    if (eliminated) continue;
                }

                string botName = nameVal.ToString();
                float scale = roomProps.TryGetValue($"{prefix}_Scale", out object sv) && sv != null
                    ? (float)sv : dm.startingScale;
                int score = dm.ScoreFromScale(scale);

                Color botColor = Color.white;
                if (prefix.StartsWith("Bot") && int.TryParse(prefix.Substring(3), out int botVid))
                {
                    foreach (var bot in EntityRegistry.Bots)
                    {
                        if (bot != null && bot.photonView != null && bot.photonView.ViewID == botVid)
                        {
                            var rend = bot.GetComponentInChildren<Renderer>();
                            if (rend != null && rend.material.HasProperty("_FresnelColor"))
                                botColor = rend.material.GetColor("_FresnelColor");
                            break;
                        }
                    }
                }

                entries.Add((botName, score, scale, true, botColor));
            }
        }

        return entries.OrderByDescending(e => e.scale).ToList();
    }

    private int GetLocalPlayerRank(List<(string name, int score, float scale, bool isBot, Color color)> sortedEntries)
    {
        for (int i = 0; i < sortedEntries.Count; i++)
        {
            if (!sortedEntries[i].isBot && sortedEntries[i].name == PhotonNetwork.NickName)
                return i + 1;
        }
        return sortedEntries.Count > 0 ? sortedEntries.Count : 1;
    }

    // ─────────────────────────────────────────────────────────
    // 게임 결과 판정
    // ─────────────────────────────────────────────────────────

    private void GameWin()
    {
        _gameRunning = false;
        GameState.Phase = GamePhase.Result;
        // timeScale은 정지 상태로 유지 — 씬 로드 시 NetworkManager.OnSceneLoaded가 1로 복구한다.

        // 결과 씬으로 넘어갈 때도 메인→인게임처럼 로딩 화면을 거치도록 설정.
        // GameWin은 모든 클라이언트에서 실행되므로 각 클라이언트가 자신의 로딩 타겟을 직접 지정한다.
        LoadingSceneController.NextSceneName = RESULT_SCENE_NAME_ABSORB;
        LoadingSceneController.AllClientsLoad = true;

        // 흡수 애니메이션 진행 중인 봇들을 즉시 정리한다.
        // 이 Destroy 이벤트가 LoadLevel 룸 프로퍼티보다 먼저 전송되어
        // 비마스터에서 순서 역전으로 인한 "Could not find PhotonView" 에러를 방지한다.
        if (PhotonNetwork.IsMasterClient)
            DestroyAbsorbedBots();

        var sortedEntries = GetSortedScores();
        int finalRank = GetLocalPlayerRank(sortedEntries);

        Debug.Log("[GameMode] 타임 오버! 생존 성공!");

        // 결과 씬으로 색상을 가져갈 수 있도록 룸 프로퍼티에 저장
        SyncAllColorsForResult();

        StartCoroutine(LoadResultSceneAfterSync());
    }

    private void DestroyAbsorbedBots()
    {
        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || !bot.IsBeingAbsorbed) continue;
            bot.StopAllCoroutines();
            PhotonNetwork.Destroy(bot.gameObject);
        }
    }

    private IEnumerator LoadResultSceneAfterSync()
    {
        // SetCustomProperties()는 비동기다 — 서버를 왕복한 뒤에야 다른 클라이언트로 전파된다.
        // 고정 시간(0.5초)만 기다리면 네트워크가 느릴 때 색상/스케일이 누락된 채로
        // 결과 씬이 로드될 수 있다.
        //
        // [개선] 마스터가 모든 색상 write 뒤에 '동기화 토큰'(ServerTimestamp)을 마지막으로 기록한다.
        // 룸 프로퍼티는 신뢰성 있고 순서가 보장되는 채널로 전송되므로, 어떤 클라이언트든
        // 새 토큰이 도착했다면 그 앞에 보낸 색상 write도 모두 도착했음이 보장된다.
        // → 모든 클라이언트는 새 토큰이 도착할 때까지 기다린 뒤 결과 씬을 로드한다.
        //
        // 모든 클라이언트가 각자 LoadLevel을 호출하는 기존 동작을 유지한다.
        // (마스터만 로드하게 하면, 마스터가 시간 종료 전 탈락해 GameWin에 도달하지 못할 때
        //  생존자들이 결과 씬으로 넘어가지 못하고 멈추는 데드락이 발생하기 때문)
        // 토큰이 끝내 도착하지 않는 경우(지연/유실, 마스터 탈락 등)를 대비해 최대 대기 시간을 둔다.
        const float maxWait = 2f;

        // 코루틴 진입 시점의 토큰 값을 기억 → 이번에 새로 기록될 토큰과 구분(재시작/재경기 대비)
        int previousToken = GetRoomSyncToken();

        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { RESULT_SYNC_TOKEN_KEY, PhotonNetwork.ServerTimestamp } });
        }

        float elapsed = 0f;
        while (GetRoomSyncToken() == previousToken && elapsed < maxWait)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // 결과 씬으로 바로 가지 않고 로딩 화면(Loading 씬)을 거친다.
        // 로딩 타겟(RESULT_SCENE_NAME)은 GameWin에서 LoadingSceneController에 지정해 두었다.
        PhotonNetwork.LoadLevel(NetworkManager.Instance.loadingSceneName);
    }

    /// <summary>현재 룸에 기록된 결과 동기화 토큰 값. 없으면 0.</summary>
    private int GetRoomSyncToken()
    {
        if (PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RESULT_SYNC_TOKEN_KEY, out object t)
            && t is int token)
            return token;
        return 0;
    }

    /// <summary>
    /// 결과 씬에서 젤리 색을 복원할 수 있도록 로컬 플레이어와 (마스터 클라이언트만) 모든 봇의 색을 룸 프로퍼티에 저장.
    /// </summary>
    private void SyncAllColorsForResult()
    {
        if (_localPlayer != null) _localPlayer.SyncColor();

        if (PhotonNetwork.IsMasterClient)
        {
            foreach (var bot in FindObjectsByType<AIPlayerMovement>(FindObjectsSortMode.None))
            {
                if (bot == null || bot.IsEliminated) continue;
                var aiSync = bot.GetComponent<AIPlayerSync>();
                if (aiSync == null) continue;

                var rend = bot.GetComponentInChildren<Renderer>();
                if (rend == null) continue;

                Color c = rend.material.GetColor("_BaseColor_01");
                aiSync.SyncColor(c);
            }
        }
    }

    public void GameOver()
    {
        if (!_gameRunning && !_isEndingSequenceStarted) return;

        // ── Push 모드(라스트 맨 스탠딩): 로컬 플레이어 사망은 '관전 전환'일 뿐 ──
        // 권위 시뮬레이션(_gameRunning / GameState.Phase)을 절대 끄지 않는다.
        // 마스터에서 이걸 끄면 타일 붕괴(UpdateStepCollapse)·전투 검증(RPC_RequestBatHit*)·
        // 생존자 판정(CheckLastSurvivor)이 전부 멈춰, 살아남은 플레이어의 발판이 얼어붙고
        // 게임도 끝나지 않는다. 실제 종료는 CheckLastSurvivor → RPC_PushModeGameEnd가 담당.
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            if (_localPlayer != null)
            {
                _localPlayer.SyncColor();
                _localPlayer.SyncScale();
                if (_localPlayer.playerController != null)
                    _localPlayer.playerController.enabled = false; // 입력만 차단(관전)
            }
            ShowResultUI("탈락!\n관전 중...");
            Debug.Log("[GameMode] Push 모드 로컬 플레이어 탈락 — 관전 전환(권위 시뮬레이션 유지)");
            return;
        }

        // 엔딩 시퀀스(3→2→1→게임 종료!)가 이미 진행 중이면
        // 어차피 곧 결과 씬으로 넘어가므로, 시퀀스를 끊지 않고 사망만 기록한다.
        // StopAllCoroutines로 시퀀스를 죽이면 결과 씬 전환이 영영 일어나지 않는다.
        if (_isEndingSequenceStarted)
        {
            GameState.Phase = GamePhase.GameOver;

            if (_localPlayer != null)
            {
                _localPlayer.SyncColor();
                _localPlayer.SyncScale();
            }

            // 입력만 차단하고 엔딩 시퀀스는 계속 진행
            if (_localPlayer != null && _localPlayer.playerController != null)
                _localPlayer.playerController.enabled = false;

            Debug.Log("[GameMode] 엔딩 시퀀스 중 탈락 — 결과 씬 전환 대기");
            return;
        }

        // ── Push 모드: 관전 전환만, 시뮬레이션 유지 ──
        // _gameRunning을 true로 유지 →
        //  1) 마스터가 죽어도 CheckLastSurvivor 계속 동작
        //  2) RPC_PushModeGameEnd 정상 수신 → 결과 씬 전환 올바르게 진행
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            GameState.Phase = GamePhase.GameOver;

            float survived = _gameTimer;
            int min = Mathf.FloorToInt(survived / 60f);
            int sec = Mathf.FloorToInt(survived % 60f);

            if (_localPlayer != null)
            {
                _localPlayer.SyncColor();
                _localPlayer.SyncScale();
            }

            if (_localPlayer != null && _localPlayer.playerController != null)
                _localPlayer.playerController.enabled = false;

            ShowResultUI($"탈락!\n{min}분 {sec}초 생존");
            Debug.Log($"[GameMode] Push모드 탈락 — 시뮬레이션 유지, 생존시간={min}분 {sec}초");
            return;
        }

        // ── 일반 모드: 완전 정지 ──
        StopAllCoroutines();
        Time.timeScale = 1f;

        _gameRunning = false;
        GameState.Phase = GamePhase.GameOver;

        float survivedTime = gameDuration - _gameTimer;
        int minTime = Mathf.FloorToInt(survivedTime / 60f);
        int secTime = Mathf.FloorToInt(survivedTime % 60f);

        if (centerCountdownText != null) centerCountdownText.text = "";

        if (_localPlayer != null)
        {
            _localPlayer.SyncColor();
            _localPlayer.SyncScale();
        }

        ShowResultUI($"탈락!\n{minTime}분 {secTime}초 생존");
        Debug.Log($"[GameMode] 로컬 플레이어 탈락! 생존시간={minTime}분 {secTime}초");
    }

    // 💡 중복 제거: 게임 결과 UI 출력 공통화
    private void ShowResultUI(string message)
    {
        if (gameResultPanel != null) gameResultPanel.SetActive(true);
        if (resultTitleText != null) resultTitleText.text = message;
    }

    // ─────────────────────────────────────────────────────────
    // UI 업데이트
    // ─────────────────────────────────────────────────────────

    private void UpdateGameTimerUI()
    {
        if (gameTimerText == null) return;

        float displayTime = _gameTimer;
        int min = Mathf.FloorToInt(displayTime / 60f);
        int sec = Mathf.FloorToInt(displayTime % 60f);
        gameTimerText.text = $"{min:00}:{sec:00}";

        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            gameTimerText.color = Color.white;
        }
        else
        {
            if (min <= 0 && sec <= 0) gameTimerText.text = "00:00";
            gameTimerText.color = _gameTimer <= endImpendingTime ? Color.red : Color.white;
        }
    }

    private void UpdateLeaderboard()
    {
        if (leaderboardContainer == null || _leaderboardPool == null) return;

        // 💡 미리 만들어둔 헬퍼 함수 하나로 코드가 대폭 줄어듭니다.
        var entries = GetSortedScores();
        _leaderboardPool.ReturnAll(_leaderboardEntries);

        int displayCount = Mathf.Min(entries.Count, 5);

        // 로컬 플레이어가 상위 displayCount 밖에 있으면 마지막 칸을 본인 행으로 대체해
        // 자신의 이름/순위가 항상 보이도록 한다. (탈락 상태면 entries에 없으니 표시 안 됨)
        int localRank = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (!entries[i].isBot && entries[i].name == PhotonNetwork.NickName)
            {
                localRank = i;
                break;
            }
        }
        bool localOutside = localRank >= displayCount;

        for (int i = 0; i < displayCount; i++)
        {
            var (name, score, scale, isBot, color) = entries[i];
            LeaderboardEntry entryComp = _leaderboardPool.Get();
            entryComp.transform.SetAsLastSibling();
            _leaderboardEntries.Add(entryComp);

            bool isMe = !isBot && name == PhotonNetwork.NickName;
            entryComp.Setup(i + 1, name, score, isMe, color);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 외부 연동 및 유틸
    // ─────────────────────────────────────────────────────────

    public void OnClickRestartButton()
    {
        _spawned = false;
        GameState.ResetValues();
        // 재시작은 콜드 스타트로 통일(완전 Disconnect 후 재연결)해 PUN 씬/큐 상태가
        // 더럽게 남아 다음 게임이 desync되는 것을 막는다.
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.GoToMainMenu();
        else
            SceneManager.LoadScene("Main");
    }

    public void RegisterLocalPlayer(NetworkPlayerSync player) => _localPlayer = player;

    public void OnPlayerAbsorbed(NetworkPlayerSync absorbedPlayer)
    {
        Debug.Log($"[GameMode] {absorbedPlayer.photonView.Owner.NickName} 흡수됨!");
        if (_localPlayer != null && absorbedPlayer == _localPlayer) GameOver();
    }

    public void OnPlayerFellOff(NetworkPlayerSync player)
    {
        if (_localPlayer != null && player == _localPlayer)
            GameOver();
    }

    private void CheckLastSurvivor()
    {
        if (!_gameRunning || !PhotonNetwork.IsMasterClient) return;

        // 게임 시작 직후엔 봇 등록 및 "Eliminated"=false 프로퍼티 전파가 끝나지 않아
        // 생존자 수가 과소 집계되어 게임이 조기 종료될 수 있으므로 잠깐 유예한다.
        if (_gameTimer < 3f) return;

        int aliveCount = 0;

        // 마스터의 신뢰할 수 있는 시야로 '생존 플레이어' ActorNumber를 수집한다.
        var survivorActors = new List<int>();
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.TryGetValue("Eliminated", out object e) && e is bool b && b)
                continue;
            survivorActors.Add(p.ActorNumber);
            aliveCount++;
        }

        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null || bot.IsEliminated || bot.IsBeingAbsorbed) continue;
            aliveCount++;
        }

        if (aliveCount <= 1)
        {
            // 결과 씬이 신뢰할 권위적 생존자 목록을 룸 프로퍼티에 기록(마스터만).
            // RPC_PushModeGameEnd 전에 보내므로 결과 동기화 토큰보다 먼저 전파된다.
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { PUSH_SURVIVOR_ACTORS_KEY, survivorActors.ToArray() } });

            photonView.RPC(nameof(RPC_PushModeGameEnd), RpcTarget.All);
        }
    }

    [PunRPC]
    private void RPC_PushModeGameEnd()
    {
        if (!_gameRunning) return;
        _gameRunning = false;
        GameState.Phase = GamePhase.Result;

        SyncAllColorsForResult();
        StartCoroutine(PushModeEndSequence());
    }

    private IEnumerator PushModeEndSequence()
    {
        PlaySFXAudio.Instance.StopWalking();
        if (centerCountdownText != null)
        {
            centerCountdownText.gameObject.SetActive(true);
            centerCountdownText.text = gameEndText;
            centerCountdownText.transform.localScale = Vector3.one * 1.5f;
            Color c = centerCountdownText.color;
            c.a = 1f;
            centerCountdownText.color = c;
        }

        float slowDuration = 1.2f;
        float elapsed = 0f;
        while (elapsed < slowDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(1f, 0.1f, elapsed / slowDuration);
            yield return null;
        }

        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(1.0f);

        LoadingSceneController.NextSceneName = RESULT_SCENE_NAME_PUSH;
        LoadingSceneController.AllClientsLoad = true;

        if (PhotonNetwork.IsMasterClient)
            DestroyAbsorbedBots();

        StartCoroutine(LoadResultSceneAfterSync());
    }

    [PunRPC]
    public void RPC_StepTileDarken(int x, int z, int stepCount, int maxSteps)
    {
        TileCollapseManager.Instance?.DarkenStepTile(x, z, stepCount, maxSteps);
    }

    [PunRPC]
    public void RPC_StepTileCollapse(int x, int z)
    {
        TileCollapseManager.Instance?.CollapseStepTile(x, z);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer) => Debug.Log($"[GameMode] {otherPlayer.NickName} 나감");

    public float GameTimer => _gameTimer;
    public bool IsGameRunning => _gameRunning;
    public float SurvivedTime => gameDuration - _gameTimer;

    /// <summary>
    /// 서버 시간 기준 게임 경과 시간 (초). 모든 클라이언트가 동일한 값을 봅니다.
    /// "GameStartTime" 룸 프로퍼티가 없으면 -1 반환.
    /// </summary>
    public float NetworkedElapsedTime
    {
        get
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return -1f;
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            if (!props.ContainsKey("GameStartTime")) return -1f;
            int startTime = (int)props["GameStartTime"];
            return (PhotonNetwork.ServerTimestamp - startTime) / 1000f;
        }
    }
}