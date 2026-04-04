// ============================================================
// GameModeManager.cs
// ============================================================
// 역할: .io 게임 규칙 관리
//   - 제한시간 내 점수 순위 결정 (점수제)
//   - 더 큰 플레이어가 작은 플레이어 흡수 (생존제)
//   - 게임 시작/종료 처리
//   - 순위표 UI 업데이트
// ============================================================

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

public class GameModeManager : MonoBehaviourPunCallbacks
{
    // ─────────────────────────────────────────────────────────
    // 싱글톤
    // ─────────────────────────────────────────────────────────
    public static GameModeManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────
    // 인스펙터 설정
    // ─────────────────────────────────────────────────────────
    [Header("게임 설정")]
    [Tooltip("게임 제한 시간 (초)")]
    public float gameDuration = 180f;   // 3분

    [Header("UI 연결")]
    public TMPro.TextMeshProUGUI timerText;         // 남은 시간 표시
    public Transform leaderboardContainer;           // 순위표 항목들의 부모
    public GameObject leaderboardEntryPrefab;        // 순위표 한 줄 프리팹
    public GameObject gameResultPanel;               // 게임 끝 결과 패널
    public TMPro.TextMeshProUGUI resultTitleText;    // "1위!" / "패배" 텍스트

    // ─────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────
    private float _remainingTime;
    private bool _gameRunning = false;
    private NetworkPlayerSync _localPlayer;

    // 순위표 항목 캐시 (매 프레임 재생성 방지)
    private List<GameObject> _leaderboardEntries = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────
    // 게임 시작
    // ─────────────────────────────────────────────────────────

    private void Start()
    {
        _remainingTime = gameDuration;

        // 이미 방에 입장해 있으면 바로 스폰 (씬 전환 후 도착한 경우)
        // 아직 입장 전이면 OnJoinedRoom 콜백에서 스폰
        if (PhotonNetwork.InRoom)
        {
            SpawnAndStartGame();
        }
        // else: OnJoinedRoom()에서 처리
    }

    // [자동 콜백] 방 입장 완료 시 호출
    // → 씬 로드보다 방 입장이 늦게 완료되는 경우를 대비
    public override void OnJoinedRoom()
    {
        SpawnAndStartGame();
    }

    private bool _spawned = false; // 중복 스폰 방지
    private void SpawnAndStartGame()
    {
        if (_spawned) return;
        _spawned = true;

        NetworkManager.Instance?.SpawnLocalPlayer();
        NetworkManager.Instance?.SpawnBots();

        // MasterClient가 게임 시작 신호를 모든 클라이언트에 전송
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(RPC_StartGame), RpcTarget.All);
        }
    }

    [PunRPC]
    private void RPC_StartGame()
    {
        _gameRunning = true;
        Debug.Log("[GameMode] 게임 시작!");
    }

    // ─────────────────────────────────────────────────────────
    // 게임 루프
    // ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_gameRunning) return;

        // 타이머 감소
        _remainingTime -= Time.deltaTime;
        UpdateTimerUI();

        // 순위표 갱신 (0.5초마다 갱신해도 충분)
        if (Time.frameCount % 30 == 0)
        {
            UpdateLeaderboard();
        }

        // 시간 종료
        if (_remainingTime <= 0f)
        {
            _remainingTime = 0f;
            EndGame();
        }
    }

    private void UpdateTimerUI()
    {
        if (timerText == null) return;
        int min = Mathf.FloorToInt(_remainingTime / 60f);
        int sec = Mathf.FloorToInt(_remainingTime % 60f);
        timerText.text = $"{min:00}:{sec:00}";

        // 30초 미만이면 빨간색
        timerText.color = _remainingTime < 30f ? Color.red : Color.white;
    }

    // ─────────────────────────────────────────────────────────
    // 순위표 업데이트
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 모든 플레이어(+ AI 봇)의 점수를 모아 순위표 갱신
    /// </summary>
    private void UpdateLeaderboard()
    {
        // 1. 점수 수집
        // ─────────────────────
        // [PlayerEntry]: 이름, 점수, 레벨, AI 여부
        var entries = new List<(string name, int score, int level, bool isBot)>();

        // 실제 플레이어 점수 (Photon CustomProperties에서 읽음)
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            int score = 0;
            int level = 1;

            if (player.CustomProperties.ContainsKey("Score"))
                score = (int)player.CustomProperties["Score"];
            if (player.CustomProperties.ContainsKey("ScaleLevel"))
                level = (int)player.CustomProperties["ScaleLevel"];

            entries.Add((player.NickName, score, level, false));
        }

        // AI 봇 점수 (Room CustomProperties에서 읽음)
        // AIBotController.SyncBotProperties()에서 저장한 값
        var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
        foreach (var key in roomProps.Keys)
        {
            string keyStr = key.ToString();
            if (keyStr.EndsWith("_Score"))
            {
                string prefix = keyStr.Replace("_Score", "");
                string botName = roomProps.ContainsKey($"{prefix}_Name")
                    ? roomProps[$"{prefix}_Name"].ToString()
                    : "Bot";
                int botScore = (int)roomProps[keyStr];
                int botLevel = roomProps.ContainsKey($"{prefix}_Level")
                    ? (int)roomProps[$"{prefix}_Level"]
                    : 1;

                entries.Add((botName, botScore, botLevel, true));
            }
        }

        // 2. 점수 내림차순 정렬
        entries = entries.OrderByDescending(e => e.score).ToList();

        // 3. UI 업데이트
        if (leaderboardContainer == null || leaderboardEntryPrefab == null) return;

        // 기존 항목 정리
        foreach (var entry in _leaderboardEntries)
            Destroy(entry);
        _leaderboardEntries.Clear();

        // 상위 5개만 표시
        int displayCount = Mathf.Min(entries.Count, 5);
        for (int i = 0; i < displayCount; i++)
        {
            var (name, score, level, isBot) = entries[i];

            GameObject entryGo = Instantiate(leaderboardEntryPrefab, leaderboardContainer);
            _leaderboardEntries.Add(entryGo);

            // 프리팹에 LeaderboardEntry 컴포넌트가 있다고 가정
            LeaderboardEntry entryComp = entryGo.GetComponent<LeaderboardEntry>();
            if (entryComp != null)
            {
                bool isMe = !isBot && name == PhotonNetwork.NickName;
                entryComp.Setup(i + 1, name, score, level, isMe);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // 플레이어 흡수 이벤트 처리
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// NetworkPlayerSync.RPC_GetAbsorbed에서 호출
    /// </summary>
    public void OnPlayerAbsorbed(NetworkPlayerSync absorbedPlayer)
    {
        Debug.Log($"[GameMode] {absorbedPlayer.PlayerName} 흡수됨!");
        // 추가 이벤트 처리 가능 (킬 피드 UI 등)
    }

    // ─────────────────────────────────────────────────────────
    // 게임 종료
    // ─────────────────────────────────────────────────────────

    private void EndGame()
    {
        if (!_gameRunning) return;
        _gameRunning = false;

        // MasterClient가 최종 순위를 계산해서 모든 클라이언트에 알림
        if (PhotonNetwork.IsMasterClient)
        {
            // 로컬 플레이어 점수
            int myScore = _localPlayer != null ? DataManager.Instance.currentScore : 0;
            photonView.RPC(nameof(RPC_EndGame), RpcTarget.All, myScore);
        }
    }

    [PunRPC]
    private void RPC_EndGame(int winnerScore)
    {
        _gameRunning = false;

        // 결과 패널 표시
        if (gameResultPanel != null)
            gameResultPanel.SetActive(true);

        // 내 순위 계산
        if (resultTitleText != null)
        {
            int myScore = DataManager.Instance?.currentScore ?? 0;
            int myRank = GetMyRank(myScore);

            if (myRank == 1)
                resultTitleText.text = "1위! 🎉";
            else
                resultTitleText.text = $"{myRank}위";
        }

        Debug.Log("[GameMode] 게임 종료!");
    }

    private int GetMyRank(int myScore)
    {
        int rank = 1;
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties.ContainsKey("Score"))
            {
                int otherScore = (int)player.CustomProperties["Score"];
                if (otherScore > myScore) rank++;
            }
        }
        return rank;
    }

    // ─────────────────────────────────────────────────────────
    // 외부 등록
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// NetworkPlayerSync.SetupLocalPlayer()에서 호출
    /// </summary>
    public void RegisterLocalPlayer(NetworkPlayerSync player)
    {
        _localPlayer = player;
    }

    // ─────────────────────────────────────────────────────────
    // Photon 콜백
    // ─────────────────────────────────────────────────────────

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[GameMode] {otherPlayer.NickName} 나감");
        // 필요시 봇으로 대체하는 로직 추가 가능
    }
}
