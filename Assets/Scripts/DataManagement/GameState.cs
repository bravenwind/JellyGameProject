using System;
using UnityEngine;

public enum GamePhase { None, Loading, Playing, GameOver, Result }

public static class GameState
{
    // ── Events ──
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

    // ── Properties with event firing ──
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

    public static void ResetRYBColor()
    {
        CurrentRYBColor = RYBColor.white;
        CurrentDisplayColor = Color.white;
    }

    public static void Reset()
    {
        _phase = GamePhase.None;
        _currentScore = 0;
        _playerCurrentScale = 2f;
        CurrentRYBColor = RYBColor.white;
        _currentDisplayColor = Color.white;
        DetectRadius = 0f;
    }
}
