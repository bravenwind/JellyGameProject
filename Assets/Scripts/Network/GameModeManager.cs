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
    public const string RESULT_SCENE_NAME = "GameResult_io";

    [Header("UI 연결 — 순위표")]
    public Transform leaderboardContainer;
    public GameObject leaderboardEntryPrefab;

    private bool _gameRunning = false;

    [SerializeField]
    private float _gameTimer = 0f;
    private static bool _spawned = false;

    private bool _isEndingSequenceStarted = false;

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

    private void SpawnAndStartGame()
    {
        if (_spawned) return;
        if (GameState.Phase == GamePhase.Playing) return;
        _spawned = true;

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
        _gameTimer = startTime;
        GameState.Phase = GamePhase.Playing;

        if (gameResultPanel != null)
            gameResultPanel.SetActive(false);
    }

    private void Update()
    {
        if (!_gameRunning) return;

        _gameTimer -= Time.deltaTime;
        UpdateGameTimerUI();

        if (Time.frameCount % 30 == 0)
            UpdateLeaderboard();

        if (_gameTimer <= 3f && !_isEndingSequenceStarted)
        {
            _isEndingSequenceStarted = true;
            StartCoroutine(GameEndingSequenceRoutine());
        }
    }

    private IEnumerator GameEndingSequenceRoutine()
    {
        int count = 3;

        centerCountdownText.gameObject.SetActive(true);

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

        // 게임 시간 정지 및 슬로우 모션 돌입
        _gameRunning = false;
        _gameTimer = 0f;

        if (centerCountdownText != null)
        {
            centerCountdownText.text = "게임 종료!";
            StartCoroutine(AnimateCenterText(centerCountdownText));
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

        // 타임스케일 원상 복구 후 결과창 호출
        Time.timeScale = 1f;
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

            // 알파값 보간 (점점 투명해짐)
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
    private List<(string name, int score, float scale, bool isBot)> GetSortedScores()
    {
        var dm = DataManager.Instance;
        var entries = new List<(string name, int score, float scale, bool isBot)>();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties.TryGetValue("Eliminated", out object elim) && elim is bool b && b)
                continue;
            float scale = player.CustomProperties.TryGetValue("Scale", out object sc) ? (float)sc : dm.startingScale;
            int score = dm.ScoreFromScale(scale);
            entries.Add((player.NickName, score, scale, false));
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

                string botName = nameVal.ToString();
                float scale = roomProps.TryGetValue($"{prefix}_Scale", out object sv) && sv != null
                    ? (float)sv : dm.startingScale;
                int score = dm.ScoreFromScale(scale);
                entries.Add((botName, score, scale, true));
            }
        }

        return entries.OrderByDescending(e => e.scale).ToList();
    }

    private int GetLocalPlayerRank(List<(string name, int score, float scale, bool isBot)> sortedEntries)
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
        Time.timeScale = 1f;

        var sortedEntries = GetSortedScores();
        int finalRank = GetLocalPlayerRank(sortedEntries);

        Debug.Log("[GameMode] 타임 오버! 생존 성공!");

        // 결과 씬으로 색상을 가져갈 수 있도록 룸 프로퍼티에 저장
        SyncAllColorsForResult();

        StartCoroutine(LoadResultSceneAfterSync());
    }

    private IEnumerator LoadResultSceneAfterSync()
    {
        // SetCustomProperties()는 비동기 — 서버 왕복 후 다른 클라이언트에 전파됨
        // 전파 완료 전 씬을 로드하면 결과 화면에서 색상/스케일이 누락되므로 대기
        yield return new WaitForSecondsRealtime(0.5f);
        PhotonNetwork.LoadLevel(RESULT_SCENE_NAME);
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
        if (!_gameRunning && !_isEndingSequenceStarted) return; // 💡 조건 수정: 엔딩 시퀀스 중이어도 탈락 처리 허용

        // 💡 플레이어가 탈락했으므로 돌고 있던 모든 엔딩 연출/코루틴을 강제로 멈춤
        StopAllCoroutines();
        Time.timeScale = 1f; // 💡 타임스케일 원상복구

        _gameRunning = false;
        GameState.Phase = GamePhase.GameOver;

        float survived = gameDuration - _gameTimer;
        int min = Mathf.FloorToInt(survived / 60f);
        int sec = Mathf.FloorToInt(survived % 60f);

        // 💡 탈락했으니 중앙 카운트다운 텍스트는 지워줌
        if (centerCountdownText != null) centerCountdownText.text = "";

        // 탈락 시에도 결과 씬에서 색상/스케일을 복원할 수 있도록 미리 저장
        if (_localPlayer != null)
        {
            _localPlayer.SyncColor();
            _localPlayer.SyncScale();
        }

        ShowResultUI($"탈락!\n{min}분 {sec}초 생존");
        Debug.Log($"[GameMode] 로컬 플레이어 탈락! 생존시간={min}분 {sec}초");
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
        int min = Mathf.FloorToInt(_gameTimer / 60f);
        int sec = Mathf.FloorToInt(_gameTimer % 60f);
        gameTimerText.text = $"{min:00}:{sec:00}";
        if (min <= 0.0f && sec <= 0.0f)
        {
            gameTimerText.text = "00:00";
        }
        gameTimerText.color = _gameTimer <= endImpendingTime ? Color.red : Color.white;
    }

    private void UpdateLeaderboard()
    {
        if (leaderboardContainer == null || _leaderboardPool == null) return;

        // 💡 미리 만들어둔 헬퍼 함수 하나로 코드가 대폭 줄어듭니다.
        var entries = GetSortedScores();
        _leaderboardPool.ReturnAll(_leaderboardEntries);

        int displayCount = Mathf.Min(entries.Count, 5);
        for (int i = 0; i < displayCount; i++)
        {
            var (name, score, scale, isBot) = entries[i];
            LeaderboardEntry entryComp = _leaderboardPool.Get();
            entryComp.transform.SetAsLastSibling();
            _leaderboardEntries.Add(entryComp);

            bool isMe = !isBot && name == PhotonNetwork.NickName;
            entryComp.Setup(i + 1, name, score, isMe);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 외부 연동 및 유틸
    // ─────────────────────────────────────────────────────────

    public void OnClickRestartButton()
    {
        _spawned = false;
        GameState.ResetValues();
        if (PhotonNetwork.InRoom) NetworkManager.Instance.LeaveRoom();
        else SceneManager.LoadScene("Main");
    }

    public void RegisterLocalPlayer(NetworkPlayerSync player) => _localPlayer = player;

    public void OnPlayerAbsorbed(NetworkPlayerSync absorbedPlayer)
    {
        Debug.Log($"[GameMode] {absorbedPlayer.photonView.Owner.NickName} 흡수됨!");
        if (_localPlayer != null && absorbedPlayer == _localPlayer) GameOver();
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