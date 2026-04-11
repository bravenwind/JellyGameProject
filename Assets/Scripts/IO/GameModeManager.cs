// ============================================================
// GameModeManager.cs
// ============================================================
// 역할: .io 색상 서바이벌 게임 규칙 관리
//
// 타이머 구조:
//   1. 전체 게임 시간 (gameDuration) — 한 판 총 시간, 끝까지 살면 승리
//   2. 목표 색상 시간 (targetTimer) — 이 목표 달성 제한시간, 실패 시 탈락
//
// 게임 루프:
//   게임 시작 → 목표 색상 지정 → 제한시간 내 달성 → 다음 목표 → ...
//   목표 미달성 → 탈락 (게임 오버)
//   전체 시간 종료 → 생존 성공 (승리)
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

    [Header("전체 게임 시간")]
    [Tooltip("한 판의 총 시간 (초). 끝까지 살아남으면 승리")]
    public float gameDuration = 180f;

    [Header("목표 색상 제한시간")]
    [Tooltip("단색(R/Y/B) 제한 시간")]
    public float singleColorTime = 20f;
    [Tooltip("혼합색(주황/초록/보라) 제한 시간")]
    public float mixedColorTime = 30f;
    [Tooltip("흰색 목표 제한 시간")]
    public float whiteColorTime = 25f;

    [Header("흰색 목표")]
    [Tooltip("흰색 목표 출현 확률 (0~1)")]
    [Range(0f, 1f)] public float whiteChance = 0.1f;

    [Header("순도 설정")]
    [Tooltip("초기 순도 통과 기준")]
    [Range(0f, 1f)] public float initialPurityThreshold = 0.55f;
    [Tooltip("최대 순도 통과 기준")]
    [Range(0f, 1f)] public float maxPurityThreshold = 0.90f;
    [Tooltip("목표 갱신마다 순도 상승량")]
    [Range(0f, 0.1f)] public float purityIncreasePerCycle = 0.03f;

    [Header("UI 연결 — 전체 게임")]
    public TMPro.TextMeshProUGUI gameTimerText;          // 전체 남은 시간

    [Header("UI 연결 — 목표 색상")]
    public TMPro.TextMeshProUGUI targetTimerText;        // 목표 남은 시간
    public TMPro.TextMeshProUGUI targetColorNameText;    // "빨강"
    public UnityEngine.UI.Image targetColorImage;        // 목표 색상 아이콘
    public UnityEngine.UI.Image purityFillImage;         // 순도 게이지 바
    public TMPro.TextMeshProUGUI purityPercentText;      // "72% / 70%"

    [Header("UI 연결 — 결과")]
    public GameObject gameResultPanel;
    public TMPro.TextMeshProUGUI resultTitleText;

    [Header("UI 연결 — 순위표")]
    public Transform leaderboardContainer;
    public GameObject leaderboardEntryPrefab;

    // ─────────────────────────────────────────────────────────
    // 상태
    // ─────────────────────────────────────────────────────────
    private bool _gameRunning = false;
    private float _gameTimer = 0f;          // 전체 게임 남은 시간
    private float _targetTimer = 0f;        // 현재 목표 남은 시간
    private int _cycleCount = 0;            // 목표 색상이 몇 번 바뀌었는지
    private JellyColorType _targetColor = JellyColorType.None;
    private float _currentPurityThreshold;
    private bool _targetAchieved = false;
    private NetworkPlayerSync _localPlayer;
    private List<GameObject> _leaderboardEntries = new List<GameObject>();

    // 목표 색상 풀 (흰색 제외)
    private static readonly JellyColorType[] NormalTargetColors =
    {
        JellyColorType.Red, JellyColorType.Yellow, JellyColorType.Blue,
        JellyColorType.Orange, JellyColorType.Green, JellyColorType.Purple
    };

    // ─────────────────────────────────────────────────────────
    // 한국어 색상명
    // ─────────────────────────────────────────────────────────
    public static string GetColorName(JellyColorType type)
    {
        switch (type)
        {
            case JellyColorType.Red:    return "빨강";
            case JellyColorType.Yellow: return "노랑";
            case JellyColorType.Blue:   return "파랑";
            case JellyColorType.Orange: return "주황";
            case JellyColorType.Green:  return "초록";
            case JellyColorType.Purple: return "보라";
            case JellyColorType.White:  return "흰색";
            case JellyColorType.Black:  return "검정";
            default:                    return "없음";
        }
    }

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
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
            photonView.RPC(nameof(RPC_StartGame), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_StartGame()
    {
        _gameRunning = true;
        _gameTimer = gameDuration;
        _currentPurityThreshold = initialPurityThreshold;
        _cycleCount = 0;

        PlayerEvents.OnColorChanged += OnPlayerColorChanged;

        AssignNewTarget();

        Debug.Log($"[GameMode] 게임 시작! 전체시간={gameDuration}s");
    }

    private void OnDestroy()
    {
        PlayerEvents.OnColorChanged -= OnPlayerColorChanged;
    }

    // ─────────────────────────────────────────────────────────
    // 게임 루프
    // ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_gameRunning) return;

        // 1. 전체 게임 타이머 감소
        _gameTimer -= Time.deltaTime;
        UpdateGameTimerUI();

        // 2. 목표 색상 타이머 감소
        _targetTimer -= Time.deltaTime;
        UpdateTargetTimerUI();
        UpdatePurityUI();

        // 순위표 갱신 (0.5초마다)
        if (Time.frameCount % 30 == 0)
            UpdateLeaderboard();

        // 3. 전체 시간 종료 → 승리
        if (_gameTimer <= 0f)
        {
            _gameTimer = 0f;
            GameWin();
            return;
        }

        // 4. 목표 시간 종료 → 달성 여부 확인
        if (_targetTimer <= 0f)
        {
            _targetTimer = 0f;

            if (_targetAchieved)
            {
                // 달성했으면 다음 목표
                AssignNewTarget();
            }
            else
            {
                // 미달성 → 탈락
                GameOver();
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // 목표 색상 관리
    // ─────────────────────────────────────────────────────────

    private void AssignNewTarget()
    {
        _cycleCount++;
        _targetAchieved = false;

        // 순도 기준 상승
        _currentPurityThreshold = Mathf.Min(
            initialPurityThreshold + purityIncreasePerCycle * (_cycleCount - 1),
            maxPurityThreshold
        );

        // 이전 목표와 다른 색 선택
        JellyColorType prev = _targetColor;
        if (Random.value < whiteChance)
        {
            _targetColor = JellyColorType.White;
        }
        else
        {
            // 이전과 겹치지 않게 최대 10회 시도
            for (int i = 0; i < 10; i++)
            {
                _targetColor = NormalTargetColors[Random.Range(0, NormalTargetColors.Length)];
                if (_targetColor != prev) break;
            }
        }

        // 제한시간 결정
        if (_targetColor == JellyColorType.White)
            _targetTimer = whiteColorTime;
        else if (IsSingleColor(_targetColor))
            _targetTimer = singleColorTime;
        else
            _targetTimer = mixedColorTime;

        // 이미 달성 상태일 수 있으니 즉시 체크
        CheckTargetAchieved();

        UpdateTargetUI();

        Debug.Log($"[GameMode] 새 목표: {GetColorName(_targetColor)}, " +
                  $"제한시간={_targetTimer:F0}s, 순도기준={_currentPurityThreshold:P0}, " +
                  $"전체 남은시간={_gameTimer:F0}s");
    }

    private bool IsSingleColor(JellyColorType type)
        => type == JellyColorType.Red || type == JellyColorType.Yellow || type == JellyColorType.Blue;

    // ─────────────────────────────────────────────────────────
    // 목표 달성 판정
    // ─────────────────────────────────────────────────────────

    private void OnPlayerColorChanged(JellyColorType dominantType, RYBColor currentRYB)
    {
        CheckTargetAchieved();
    }

    private void CheckTargetAchieved()
    {
        if (_targetAchieved || !_gameRunning) return;

        RYBColor current = DataManager.Instance.currentRYBColor;
        JellyColorType dominant = current.GetDominantType();
        float purity = current.GetPurity(_targetColor);

        if (dominant == _targetColor && purity >= _currentPurityThreshold)
        {
            _targetAchieved = true;
            PlayerEvents.OnTargetColorChecked?.Invoke(true);
            Debug.Log($"[GameMode] 목표 달성! {GetColorName(_targetColor)} 순도={purity:P0}");
        }
        else
        {
            PlayerEvents.OnTargetColorChecked?.Invoke(false);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 게임 종료
    // ─────────────────────────────────────────────────────────

    private void GameOver()
    {
        _gameRunning = false;

        float survived = gameDuration - _gameTimer;
        int min = Mathf.FloorToInt(survived / 60f);
        int sec = Mathf.FloorToInt(survived % 60f);

        Debug.Log($"[GameMode] 탈락! {GetColorName(_targetColor)} 미달성, 생존시간={min}분 {sec}초");

        if (gameResultPanel != null)
            gameResultPanel.SetActive(true);

        if (resultTitleText != null)
            resultTitleText.text = $"탈락\n{min}분 {sec}초 생존";
    }

    private void GameWin()
    {
        _gameRunning = false;

        Debug.Log("[GameMode] 승리! 끝까지 생존!");

        if (gameResultPanel != null)
            gameResultPanel.SetActive(true);

        if (resultTitleText != null)
            resultTitleText.text = "생존 성공!";
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

    private void UpdateTargetTimerUI()
    {
        if (targetTimerText == null) return;
        int sec = Mathf.CeilToInt(Mathf.Max(_targetTimer, 0f));
        targetTimerText.text = $"{sec}";
        targetTimerText.color = _targetTimer < 5f ? Color.red : Color.white;
    }

    private void UpdateTargetUI()
    {
        if (targetColorNameText != null)
            targetColorNameText.text = GetColorName(_targetColor);

        if (targetColorImage != null)
            targetColorImage.color = RYBColor.GetTargetRGB(_targetColor);
    }

    private void UpdatePurityUI()
    {
        if (purityFillImage == null && purityPercentText == null) return;

        float purity = DataManager.Instance.GetCurrentPurity(_targetColor);

        if (purityFillImage != null)
        {
            purityFillImage.fillAmount = purity;
            purityFillImage.color = purity >= _currentPurityThreshold
                ? new Color(0.2f, 0.9f, 0.3f)
                : new Color(0.9f, 0.3f, 0.2f);
        }

        if (purityPercentText != null)
            purityPercentText.text = $"{purity * 100f:F0}% / {_currentPurityThreshold * 100f:F0}%";
    }

    // ─────────────────────────────────────────────────────────
    // 순위표
    // ─────────────────────────────────────────────────────────

    private void UpdateLeaderboard()
    {
        var entries = new List<(string name, int score, int level, bool isBot)>();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            int score = 0, level = 1;
            if (player.CustomProperties.ContainsKey("Score"))
                score = (int)player.CustomProperties["Score"];
            if (player.CustomProperties.ContainsKey("ScaleLevel"))
                level = (int)player.CustomProperties["ScaleLevel"];
            entries.Add((player.NickName, score, level, false));
        }

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
                int botLevel = roomProps.ContainsKey($"{prefix}_Level")
                    ? (int)roomProps[$"{prefix}_Level"] : 1;
                entries.Add((botName, botScore, botLevel, true));
            }
        }

        entries = entries.OrderByDescending(e => e.score).ToList();

        if (leaderboardContainer == null || leaderboardEntryPrefab == null) return;

        foreach (var entry in _leaderboardEntries) Destroy(entry);
        _leaderboardEntries.Clear();

        int displayCount = Mathf.Min(entries.Count, 5);
        for (int i = 0; i < displayCount; i++)
        {
            var (name, score, level, isBot) = entries[i];
            GameObject entryGo = Instantiate(leaderboardEntryPrefab, leaderboardContainer);
            _leaderboardEntries.Add(entryGo);

            LeaderboardEntry entryComp = entryGo.GetComponent<LeaderboardEntry>();
            if (entryComp != null)
            {
                bool isMe = !isBot && name == PhotonNetwork.NickName;
                entryComp.Setup(i + 1, name, score, level, isMe);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // 외부
    // ─────────────────────────────────────────────────────────

    public void RegisterLocalPlayer(NetworkPlayerSync player) => _localPlayer = player;
    public void OnPlayerAbsorbed(NetworkPlayerSync absorbedPlayer)
        => Debug.Log($"[GameMode] {absorbedPlayer.PlayerName} 흡수됨!");

    public override void OnPlayerLeftRoom(Player otherPlayer)
        => Debug.Log($"[GameMode] {otherPlayer.NickName} 나감");

    // ─────────────────────────────────────────────────────────
    // 공개 프로퍼티
    // ─────────────────────────────────────────────────────────

    public JellyColorType TargetColor => _targetColor;
    public float GameTimer => _gameTimer;
    public float TargetTimer => _targetTimer;
    public int CycleCount => _cycleCount;
    public float CurrentPurityThreshold => _currentPurityThreshold;
    public bool IsTargetAchieved => _targetAchieved;
    public bool IsGameRunning => _gameRunning;

    /// <summary>총 생존 시간</summary>
    public float SurvivedTime => gameDuration - _gameTimer;
}
