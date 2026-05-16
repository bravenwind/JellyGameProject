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

public class GameModeManager : MonoBehaviourPunCallbacks
{
    public static GameModeManager Instance { get; private set; }

    [Header("전체 게임 시간")]
    public float gameDuration = 180f;
    public float endImpendingTime = 10.0f;

    [Header("UI 연결 — 전체 게임")]
    public TextMeshProUGUI gameTimerText;

    [Header("UI 연결 — 결과")]
    public GameObject gameResultPanel;
    public TextMeshProUGUI resultTitleText;

    [Header("UI 연결 — 순위표")]
    public Transform leaderboardContainer;
    public GameObject leaderboardEntryPrefab;

    private bool _gameRunning = false;
    private float _gameTimer = 0f;
    private static bool _spawned = false;

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

        NetworkManager.Instance?.SpawnLocalPlayer();
        NetworkManager.Instance?.SpawnBots();

        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(RPC_StartGame), RpcTarget.All);
        }
        else
        {
            TryJoinRunningGame();
        }
    }

    private void TryJoinRunningGame()
    {
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        if (props.ContainsKey("GameStartTime"))
        {
            int startTime = (int)props["GameStartTime"];
            float elapsed = (PhotonNetwork.ServerTimestamp - startTime) / 1000f;
            float remaining = gameDuration - elapsed;

            if (remaining > 0f)
            {
                StartGameInternal(remaining);
            }
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
        GameState.Reset(); // 이전 게임의 점수/색상/스케일/이벤트 구독 초기화
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

        if (_gameTimer <= 0f)
        {
            _gameTimer = 0f;
            GameWin();
        }
    }

    // ─────────────────────────────────────────────────────────
    // 공통 데이터 헬퍼 (💡 중복 제거의 핵심)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 방의 모든 플레이어와 봇의 점수를 가져와 내림차순으로 정렬하여 반환
    /// </summary>
    private List<(string name, int score, bool isBot)> GetSortedScores()
    {
        var entries = new List<(string name, int score, bool isBot)>();

        // 1. 유저 점수
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            int score = player.CustomProperties.TryGetValue("Score", out object s) ? (int)s : 0;
            entries.Add((player.NickName, score, false));
        }

        // 2. 봇 점수
        var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
        foreach (var key in roomProps.Keys)
        {
            string keyStr = key.ToString();
            if (keyStr.EndsWith("_Score"))
            {
                string prefix = keyStr.Replace("_Score", "");
                string botName = roomProps.ContainsKey($"{prefix}_Name") ? roomProps[$"{prefix}_Name"].ToString() : "Bot";
                int botScore = (int)roomProps[keyStr];

                entries.Add((botName, botScore, true));
            }
        }

        return entries.OrderByDescending(e => e.score).ToList();
    }

    private int GetLocalPlayerRank(List<(string name, int score, bool isBot)> sortedEntries)
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

        var sortedEntries = GetSortedScores();
        int finalRank = GetLocalPlayerRank(sortedEntries);

        ShowResultUI($"시간 종료!\n최종 순위 : {finalRank}위");
        Debug.Log("[GameMode] 타임 오버! 생존 성공!");
    }

    public void GameOver()
    {
        if (!_gameRunning) return;
        _gameRunning = false;
        GameState.Phase = GamePhase.GameOver;

        float survived = gameDuration - _gameTimer;
        int min = Mathf.FloorToInt(survived / 60f);
        int sec = Mathf.FloorToInt(survived % 60f);

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
            var (name, score, isBot) = entries[i];
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
}