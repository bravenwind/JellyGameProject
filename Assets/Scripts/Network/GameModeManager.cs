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
    public string gameStartText = "시작!";
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
    private bool _countdownRunning = false;

    // 로컬 플레이어가 이번 판에서 탈락(관전 전환)했는지. GameOver() 중복 처리 방지용 —
    // 관전 전환 후에도 시뮬레이션은 계속 돌므로 이 플래그가 유일한 사망 기록이다.
    private bool _localPlayerOut = false;

    // 시작 카운트다운(3-2-1)이 진행 중인지. AI/입력 정지에 쓰인다.
    // GameState.Phase와 달리 '로컬 플레이어 사망'에 영향받지 않아, 카운트다운만 정확히 가린다.
    public static bool CountdownActive = false;

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
        CountdownActive = false;
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

        // 새 게임 씬은 항상 '입력 잠금 해제' 상태로 시작한다. (StartCountdownRoutine이 다시 잠근다.)
        // CountdownActive/InputLocked는 SubsystemRegistration에서만 리셋되므로, 도메인 리로드 없는
        // 씬 리로드만으로는 안 풀린다 → 카운트다운 도중 씬이 바뀌면 다음 씬에 잠금이 새어 들어올 수 있어
        // 씬 진입 시점에 한 번 더 확실히 푼다. (J5)
        CountdownActive = false;
        PlayerMovement.InputLocked = false;
    }

    private void OnDestroy()
    {
        _spawned = false;
        Time.timeScale = 1f;

        // 카운트다운 코루틴이 도중에 끊겨도(씬 전환·파괴) 전역 잠금 플래그가 남지 않도록 정리한다. (J5)
        CountdownActive = false;
        PlayerMovement.InputLocked = false;
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

        // 룸 커스텀 프로퍼티(권위값)로부터 실제 게임 모드를 복원한다.
        // ※ 과거엔 DataManager.Awake의 GameState.Reset()이 모드를 Absorb로 되돌려 이 복원이
        //   필수였다(빌드 클라가 Push인데 좌클릭 공격 불가). W1/V3 수정으로 Awake가 모드를
        //   건드리지 않게 됐지만, "씬 진입 시 룸 권위값으로 재확인"은 그 자체로 올바른
        //   방어(단일 출처 원칙)라 유지한다.
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
    }

    // 💡 게임 시작 공통 초기화 + 3-2-1 카운트다운 시작.
    // 카운트다운 동안엔 아직 '시작 전' 상태로 둔다(_gameRunning=false / Phase!=Playing):
    //  - 타일 붕괴·게임 타이머 정지(_gameRunning)
    //  - 흡수/공격/동기화 차단(Phase!=Playing 가드는 NetworkPlayerSync 전반에 이미 존재)
    //  - 로컬 입력 잠금(PlayerMovement.InputLocked) — Idle 애니는 그대로 재생
    private void StartGameInternal(float startTime)
    {
        GameState.ResetValues();              // Phase=None
        _gameRunning = false;
        _localPlayerOut = false;              // 새 판 — 사망 기록 초기화
        CountdownActive = true;               // 카운트다운 시작 — 봇/입력 정지(RPC 도착 즉시)
        _gameTimer = (GameState.CurrentGameMode == GameModeType.Push) ? 0f : startTime;

        if (gameResultPanel != null)
            gameResultPanel.SetActive(false);

        if (!_countdownRunning)
            StartCoroutine(StartCountdownRoutine());
    }

    /// <summary>3-2-1 카운트다운 → "시작!" 표시와 동시에 게임을 개시한다.</summary>
    private IEnumerator StartCountdownRoutine()
    {
        _countdownRunning = true;
        PlayerMovement.InputLocked = true;    // 이동/대쉬/공격/점프 차단(로컬). Idle 애니는 유지.

        if (centerCountdownText != null)
            centerCountdownText.gameObject.SetActive(true);

        for (int n = 3; n >= 1; n--)
        {
            if (centerCountdownText != null)
            {
                centerCountdownText.text = n.ToString();
                StartCoroutine(AnimateCenterText(centerCountdownText));
            }
            yield return new WaitForSecondsRealtime(1f);
        }

        // [Y3] 카운트다운 도중 게임이 이미 끝나버렸다면(지연 클라가 종료 RPC를 먼저 처리)
        // 여기서 Playing으로 되돌려 결과 전환을 뒤엎지 않는다.
        if (GameState.Phase == GamePhase.Result)
        {
            CountdownActive = false;
            PlayerMovement.InputLocked = false;
            _countdownRunning = false;
            if (centerCountdownText != null) centerCountdownText.gameObject.SetActive(false);
            yield break;
        }

        // "시작!" 표시와 동시에 실제 게임 시작
        if (centerCountdownText != null)
        {
            centerCountdownText.text = gameStartText;
            StartCoroutine(AnimateCenterText(centerCountdownText));
        }

        _gameRunning = true;
        GameState.Phase = GamePhase.Playing;
        CountdownActive = false;
        PlayerMovement.InputLocked = false;

        // 붕괴 타이밍 기준점은 카운트다운을 제외한 '실제 시작' 시점으로 기록한다(마스터만).
        if (PhotonNetwork.IsMasterClient)
        {
            Hashtable props = new Hashtable { { "GameStartTime", PhotonNetwork.ServerTimestamp } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        Debug.Log($"[GameMode] 게임 시작! 전체시간={gameDuration}s");

        // 시작 텍스트 잠깐 보여주고 정리
        yield return new WaitForSecondsRealtime(0.7f);
        if (centerCountdownText != null)
            centerCountdownText.gameObject.SetActive(false);

        _countdownRunning = false;
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

        // [Y1] 남은 시간(=종료 판정)을 서버 클럭에 고정한다.
        // 로컬 deltaTime 누적은 RPC 도착 지연·프레임 히칭으로 클라마다 어긋나
        // 종료 순간이 수 초씩 갈렸다(한쪽은 Result 동결, 다른 쪽은 아직 Playing).
        // GameStartTime 룸 프로퍼티(마스터가 카운트다운 종료 시각을 기록)는 모든 클라가
        // 같은 값을 읽으므로, 이를 기준으로 하면 전 클라의 만료 순간이 일치한다.
        // 룸 프로퍼티가 아직 도착 전이면(-1) 종전처럼 로컬 차감으로 폴백.
        float networkElapsed = NetworkedElapsedTime;
        if (networkElapsed >= 0f)
            _gameTimer = Mathf.Max(0f, gameDuration - networkElapsed);
        else
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
    /// 현재 방의 모든 생존 플레이어/봇을 크기 내림차순으로 반환.
    /// [H6] 수집·필터·정렬은 ScoreboardSnapshot으로 통일했고, 여기는 '살아있는
    /// 오브젝트에서 색을 읽는' 인게임 전용 리졸버만 제공한다. (결과 씬은 프로퍼티에서 읽음)
    /// </summary>
    private List<ScoreboardSnapshot.Entry> GetSortedScores()
    {
        return ScoreboardSnapshot.Collect(
            DataManager.Instance.startingScale,
            ResolveLivePlayerColor,
            ResolveLiveBotColor);
    }

    private static Color ResolveLivePlayerColor(Player player)
    {
        foreach (var nps in EntityRegistry.Players)
        {
            if (nps != null && nps.photonView.Owner?.ActorNumber == player.ActorNumber)
                return nps.DisplayColor;
        }
        return Color.white;
    }

    private static Color ResolveLiveBotColor(string prefix, int viewId)
    {
        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot != null && bot.photonView != null && bot.photonView.ViewID == viewId)
            {
                // 읽기 전용 조회이므로 sharedMaterial 사용 — .material은 봇마다
                // 머티리얼 인스턴스를 복제해 배칭을 깨뜨린다(G4와 동일한 학습 포인트).
                var rend = bot.GetComponentInChildren<Renderer>();
                if (rend != null && rend.sharedMaterial != null
                    && rend.sharedMaterial.HasProperty("_FresnelColor"))
                    return rend.sharedMaterial.GetColor("_FresnelColor");
                break;
            }
        }
        return Color.white;
    }

    // [Y5 정리] GetLocalPlayerRank 삭제 (2026-07-22) — GameWin에서 계산만 하고 버리던
    // 데드 코드였다. '내 최종 순위' 표시(Y6)를 붙일 때는 닉네임 문자열 비교(Q4) 대신
    // ScoreboardSnapshot.Entry.actorNumber == PhotonNetwork.LocalPlayer.ActorNumber로 새로 작성할 것.

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

            // [Y2] 이 시점은 이미 Phase == Result라 AIPlayerSync.OnDestroy가
            // 룸 프로퍼티 정리를 건너뛴다(생존 봇의 결과용 데이터 보존 목적).
            // 흡수로 소멸하는 봇은 여기서 명시적으로 지워야 — 안 지우면 Bot*_Name 등이
            // 룸에 남아 결과 씬 ScoreboardSnapshot이 '살아있는 봇'으로 오인해
            // 파괴된 봇이 시상대에 오르고 실제 생존자를 밀어낸다(유령 봇).
            bot.GetComponent<AIPlayerSync>()?.ClearBotProperties();

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

                // [Y4] 읽기 전용 조회는 sharedMaterial — .material 게터는 봇마다 머티리얼
                // 인스턴스를 복제해(G4) 종료 순간 프레임 스파이크를 만든다.
                // 키는 결과 씬 젤리 '몸색' 복원용이므로 _BaseColor_01이 맞다
                // (_FresnelColor는 리더보드 이름색용 림라이트 색 — ResolveLiveBotColor 참조).
                if (rend.sharedMaterial == null || !rend.sharedMaterial.HasProperty("_BaseColor_01")) continue;
                Color c = rend.sharedMaterial.GetColor("_BaseColor_01");
                aiSync.SyncColor(c);
            }
        }
    }

    public void GameOver()
    {
        if (!_gameRunning && !_isEndingSequenceStarted) return;

        // 중복 사망 처리 방지 — 관전 전환 후에도 시뮬레이션(_gameRunning)은 계속 돌므로,
        // 낙하 감지 등이 매 프레임 GameOver를 재호출해도 한 번만 처리한다.
        if (_localPlayerOut) return;
        _localPlayerOut = true;

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
            // Phase는 건드리지 않는다 — 여기서 GameOver로 바꾸면 (죽은 클라가 마스터일 때)
            // 엔딩 카운트다운 마지막 3초 동안 흡수 검증 RPC가 전부 거부된다(아래 Absorb 분기와 동일 원리).
            // 시퀀스 종료 시 Phase=Result는 GameEndingSequenceRoutine이 책임진다.

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

        // ── Absorb 모드: 로컬 플레이어 사망 → 관전 전환 (Push 분기와 동일 원칙) ──
        // [중요] 권위 시뮬레이션(_gameRunning / GameState.Phase)을 절대 끄지 않는다.
        // 죽은 클라가 '마스터'면 이 두 값이 곧 심판 서비스의 전원 스위치이기 때문:
        //  - Phase=GameOver로 바꾸면 → RPC_Request(Bot)AbsorbValidation의 Phase!=Playing
        //    가드에 걸려 이후 모든 흡수 요청이 조용히 거부되고("내가 큰데도 안 먹어짐"),
        //    흡수 확정 봇의 PhotonNetwork.Destroy(Phase==Playing 조건)도 함께 차단된다.
        //  - _gameRunning=false로 끄면 → TileCollapseManager.Update(IsGameRunning 가드)가
        //    마스터에서만 멈춘다. 링 붕괴는 클라별 로컬(동기 시계) 진행이라, 마스터 땅만
        //    멀쩡히 남고 봇들(마스터 NavMesh 기준 이동)이 산 클라 화면의 구멍 위를 걸어다닌다.
        // 입력만 차단하고 시뮬레이션을 유지하면, 죽은 클라도 Update가 계속 돌아 시간 종료 시
        // GameEndingSequence→GameWin을 산 클라와 똑같이 정상 실행한다(관전 연출 일관).

        // 방어적 프리셋: 어떤 이유로든 이 클라가 GameWin에 못 도달해도(예외 등)
        // AutomaticallySyncScene에 끌려갈 때 올바른 결과 로딩 화면을 거치도록 미리 지정.
        // (GameWin이 정상 실행되면 같은 값으로 다시 세팅되므로 무해)
        LoadingSceneController.NextSceneName = RESULT_SCENE_NAME_ABSORB;
        LoadingSceneController.AllClientsLoad = true;

        float survivedTime = gameDuration - _gameTimer;
        int minTime = Mathf.FloorToInt(survivedTime / 60f);
        int secTime = Mathf.FloorToInt(survivedTime % 60f);

        if (centerCountdownText != null) centerCountdownText.text = "";

        if (_localPlayer != null)
        {
            _localPlayer.SyncColor();
            _localPlayer.SyncScale();
            if (_localPlayer.playerController != null)
                _localPlayer.playerController.enabled = false; // 입력만 차단(관전) — Push 분기와 동일
        }

        ShowResultUI($"탈락!\n{minTime}분 {secTime}초 생존");
        Debug.Log($"[GameMode] Absorb 로컬 플레이어 탈락 — 관전 전환(권위 시뮬레이션 유지). 생존시간={minTime}분 {secTime}초");
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
            var entry = entries[i];
            LeaderboardEntry entryComp = _leaderboardPool.Get();
            entryComp.transform.SetAsLastSibling();
            _leaderboardEntries.Add(entryComp);

            bool isMe = !entry.isBot && entry.name == PhotonNetwork.NickName;
            int score = DataManager.Instance.ScoreFromScale(entry.scale);
            entryComp.Setup(i + 1, entry.name, score, isMe, entry.color);
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
            LoadingSceneController.LoadMainViaLoading();
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
            // [P1] PlayerTtl > 0 도입 후 PlayerList에는 '끊겨서 자리만 보존된'(Inactive)
            // 플레이어도 남는다 — 생존자 수에 넣으면 게임이 영영 안 끝난다.
            if (p.IsInactive) continue;
            if (p.CustomProperties.TryGetValue(NetworkPlayerSync.ELIMINATED_KEY, out object e) && e is bool b && b)
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
        // [Y3] '이미 결과로 전환했는가'(중복 수신)만 걸러낸다.
        // 예전 가드(!_gameRunning)는 자기 카운트다운이 아직 안 끝난(지연 입장/씬 재동기)
        // 클라이언트의 종료 신호까지 버려, 그 클라만 끝난 게임 씬에 영영 갇혔다.
        // 종료 신호는 '시작했는지'와 무관하게 반드시 처리돼야 데드락이 없다.
        if (GameState.Phase == GamePhase.Result) return;
        _gameRunning = false;
        GameState.Phase = GamePhase.Result;

        SyncAllColorsForResult();
        StartCoroutine(PushModeEndSequence());
    }

    private IEnumerator PushModeEndSequence()
    {
        PlaySFXAudio.Instance?.StopWalking();
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