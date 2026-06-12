using System;
using UnityEngine;

public enum GamePhase { None, Loading, Playing, GameOver, Result }

public enum GameModeType { Absorb, Push }

public static class GameState
{
    // ── Events   ──
    public static event Action<GamePhase> OnPhaseChanged;
    public static event Action<int> OnScoreChanged;
    public static event Action<float> OnScaleChanged;
    public static event Action<Color> OnDisplayColorChanged;

    // ── Backing fields ──
    private static GamePhase _phase = GamePhase.None;
    private static int _currentScore;
    private static float _playerCurrentScale = 2f;
    private static RYBColor _currentRYBColor = RYBColor.white;
    private static Color _currentDisplayColor = Color.white;

    // ── Properties & Event invoking ──
    public static GamePhase Phase
    {
        get => _phase;
        set
        {
            if (_phase == value) return;
            _phase = value;
            OnPhaseChanged?.Invoke(_phase);
        }
    }

    public static int CurrentScore
    {
        get => _currentScore;
        set
        {
            if (_currentScore == value) return;
            _currentScore = value;
            OnScoreChanged?.Invoke(_currentScore);
        }
    }

    public static float PlayerCurrentScale
    {
        get => _playerCurrentScale;
        set
        {
            if (Mathf.Approximately(_playerCurrentScale, value)) return;
            _playerCurrentScale = value;
            OnScaleChanged?.Invoke(_playerCurrentScale);
        }
    }

    public static RYBColor CurrentRYBColor { get; set; } = RYBColor.white;

    public static Color CurrentDisplayColor
    {
        get => _currentDisplayColor;
        set
        {
            _currentDisplayColor = value;
            OnDisplayColorChanged?.Invoke(_currentDisplayColor);
        }
    }

    public static float DetectRadius { get; set; }

    public static GameModeType CurrentGameMode { get; set; } = GameModeType.Absorb;

    public static void ResetRYBColor()
    {
        CurrentRYBColor = RYBColor.white;
        CurrentDisplayColor = Color.white;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        _phase = GamePhase.None;
        _currentScore = 0;
        _playerCurrentScale = 2f;
        CurrentRYBColor = RYBColor.white;
        _currentDisplayColor = Color.white;
        DetectRadius = 0f;
        CurrentGameMode = GameModeType.Absorb;

        OnPhaseChanged = null;
        OnScoreChanged = null;
        OnScaleChanged = null;
        OnDisplayColorChanged = null;
    }

    public static void ResetValues()
    {
        _phase = GamePhase.None;
        _currentScore = 0;
        _playerCurrentScale = 2f;
        CurrentRYBColor = RYBColor.white;
        _currentDisplayColor = Color.white;
        DetectRadius = 0f;

        // [H1] 여기서 이벤트를 null로 비우면 안 된다.
        // 씬 UI(LevelUI/ScoreUI 등)는 OnEnable(씬 활성화)에서 구독하는데, 게임 시작 RPC가
        // 이 함수를 그 뒤에 호출하므로 구독이 통째로 끊겨 UI 갱신이 멈춘다.
        // 구독 해제는 각 구독자의 OnDisable 책임이고, 전체 정리는 도메인 리로드 대비용인
        // Reset()(SubsystemRegistration)에서만 수행한다.
    }
}
