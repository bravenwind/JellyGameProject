// ============================================================
// GameModeManager.cs (색상 목표 제거 버전)
// ============================================================
// 역할: .io 기본 서바이벌 게임 규칙 관리
//
// 게임 루프:
//   게임 시작 → 제한 시간(gameDuration) 동안 생존 및 점수 경쟁
//   도중에 흡수당함 → 탈락 (게임 오버)
//   전체 시간 종료 → 생존 성공 (승리 및 순위 발표)
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
    // ─────────────────────────────────────────────────────────
    // 싱글톤
    // ─────────────────────────────────────────────────────────
    public static GameModeManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────
    // 인스펙터 설정
    // ─────────────────────────────────────────────────────────
    [Header("전체 게임 시간")]
    [Tooltip("한 판의 총 시간 (초). 끝까지 살아남으면 승리")]
    public float gameDuration = 180f;

    [Header("UI 연결 — 전체 게임")]
    public TextMeshProUGUI gameTimerText;          // 전체 남은 시간

    [Header("UI 연결 — 결과")]
    public GameObject gameResultPanel;
    public TextMeshProUGUI resultTitleText;

    [Header("UI 연결 — 순위표")]
    public Transform leaderboardContainer;
    public GameObject leaderboardEntryPrefab;

    // ─────────────────────────────────────────────────────────
    // 상태
    // ─────────────────────────────────────────────────────────
    private bool _gameRunning = false;
    private float _gameTimer = 0f;          // 전체 게임 남은 시간

    // 이 클라이언트의 로컬 플레이어 참조 (흡수당했는지 체크하기 위함)
    private NetworkPlayerSync _localPlayer;
    private List<LeaderboardEntry> _leaderboardEntries = new List<LeaderboardEntry>();
    private ObjectPool<LeaderboardEntry> _leaderboardPool;

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────
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
        if (PhotonNetwork.InRoom)
            SpawnAndStartGame();
    }

    public override void OnJoinedRoom()
    {
        SpawnAndStartGame();
    }

    private bool _spawned = false;
    private void SpawnAndStartGame()
    {
        if (_spawned) return;
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
                _gameRunning = true;
                _gameTimer = remaining;
                GameState.Phase = GamePhase.Playing;

                if (gameResultPanel != null)
                    gameResultPanel.SetActive(false);
            }
            else
            {
                _gameTimer = 0f;
                GameWin();
            }
        }
    }

    [PunRPC]
    private void RPC_StartGame()
    {
        _gameRunning = true;
        _gameTimer = gameDuration;
        GameState.Phase = GamePhase.Playing;

        if (gameResultPanel != null)
            gameResultPanel.SetActive(false);

        if (PhotonNetwork.IsMasterClient)
        {
            Hashtable props = new Hashtable { { "GameStartTime", PhotonNetwork.ServerTimestamp } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        Debug.Log($"[GameMode] 게임 시작! 전체시간={gameDuration}s");
    }

    // ─────────────────────────────────────────────────────────
    // 게임 루프
    // ─────────────────────────────────────────────────────────
    private void Update()
    {
        if (!_gameRunning) return;

        // 1. 전체 타이머 감소
        _gameTimer -= Time.deltaTime;
        UpdateGameTimerUI();

        // 2. 순위표 갱신 (0.5초마다 연산 부하를 줄이기 위해 프레임 나눔)
        if (Time.frameCount % 30 == 0)
            UpdateLeaderboard();

        // 3. 전체 시간 종료 → 생존자 승리 처리
        if (_gameTimer <= 0f)
        {
            _gameTimer = 0f;
            GameWin();
        }
    }

    // ─────────────────────────────────────────────────────────
    // 게임 결과 판정 (승리 / 탈락)
    // ─────────────────────────────────────────────────────────
    private void GameWin()
    {
        _gameRunning = false;
        GameState.Phase = GamePhase.Result;
        Debug.Log("[GameMode] 타임 오버! 생존 성공!");

        if (gameResultPanel != null)
            gameResultPanel.SetActive(true);

        if (resultTitleText != null)
            resultTitleText.text = "시간 종료\n최종 순위를 확인하세요!";
    }

    public void GameOver()
    {
        if (!_gameRunning) return;
        _gameRunning = false;
        GameState.Phase = GamePhase.GameOver;

        float survived = gameDuration - _gameTimer;
        int min = Mathf.FloorToInt(survived / 60f);
        int sec = Mathf.FloorToInt(survived % 60f);

        Debug.Log($"[GameMode] 로컬 플레이어 탈락! 생존시간={min}분 {sec}초");

        if (gameResultPanel != null)
            gameResultPanel.SetActive(true);

        if (resultTitleText != null)
            resultTitleText.text = $"탈락\n{min}분 {sec}초 생존";
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
        gameTimerText.color = _gameTimer < 30f ? Color.red : Color.white;
    }

    // ─────────────────────────────────────────────────────────
    // 순위표
    // ─────────────────────────────────────────────────────────
    private void UpdateLeaderboard()
    {
        // 튜플에서 level 제거: (이름, 점수, 봇 여부)
        var entries = new List<(string name, int score, bool isBot)>();

        // 1. 실제 유저 정보 가져오기
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            int score = 0;
            if (player.CustomProperties.ContainsKey("Score"))
                score = (int)player.CustomProperties["Score"];

            entries.Add((player.NickName, score, false));
        }

        // 2. 봇 정보 가져오기
        var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
        foreach (var key in roomProps.Keys)
        {
            string keyStr = key.ToString();
            if (keyStr.EndsWith("_Score"))
            {
                string prefix = keyStr.Replace("_Score", "");
                string botName = roomProps.ContainsKey($"{prefix}_Name")
                    ? roomProps[$"{prefix}_Name"].ToString() : "Bot";
                int botScore = (int)roomProps[keyStr];

                // 레벨 가져오는 부분 삭제함
                entries.Add((botName, botScore, true));
            }
        }

        // 3. 점수 기준 내림차순 정렬
        entries = entries.OrderByDescending(e => e.score).ToList();

        if (leaderboardContainer == null || _leaderboardPool == null) return;

        _leaderboardPool.ReturnAll(_leaderboardEntries);

        int displayCount = Mathf.Min(entries.Count, 5);
        for (int i = 0; i < displayCount; i++)
        {
            var (name, score, isBot) = entries[i];
            LeaderboardEntry entryComp = _leaderboardPool.Get();
            _leaderboardEntries.Add(entryComp);

            bool isMe = !isBot && name == PhotonNetwork.NickName;
            entryComp.Setup(i + 1, name, score, isMe);
        }
    }

    public void OnClickRestartButton()
    {
        // 1. 만약 현재 방에 있다면 방에서 나가는 요청을 보냄
        if (PhotonNetwork.InRoom)
        {
            NetworkManager.Instance.LeaveRoom();
        }
        else
        {
            // 방에 없는 상태라면 바로 씬 이동
            SceneManager.LoadScene("Main"); 
        }
    }

    // ─────────────────────────────────────────────────────────
    // 외부 연동 로직
    // ─────────────────────────────────────────────────────────
    public void RegisterLocalPlayer(NetworkPlayerSync player)
    {
        _localPlayer = player;
    }

    /// <summary>
    /// NetworkPlayerSync에서 로컬 플레이어가 먹혔을 때 호출해 주어야 함.
    /// </summary>
    public void OnPlayerAbsorbed(NetworkPlayerSync absorbedPlayer)
    {
        Debug.Log($"[GameMode] {absorbedPlayer.photonView.Owner.NickName} 흡수됨!");

        // 만약 흡수당한 대상이 나 자신(LocalPlayer)이라면 게임 오버 처리
        if (_localPlayer != null && absorbedPlayer == _localPlayer)
        {
            GameOver();
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[GameMode] {otherPlayer.NickName} 나감");
    }

    // ─────────────────────────────────────────────────────────
    // 공개 프로퍼티
    // ─────────────────────────────────────────────────────────
    public float GameTimer => _gameTimer;
    public bool IsGameRunning => _gameRunning;

    /// <summary>총 생존 시간</summary>
    public float SurvivedTime => gameDuration - _gameTimer;
}