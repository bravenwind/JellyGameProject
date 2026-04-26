using UnityEngine;

public enum GamePhase { None, Loading, Playing, GameOver, Result }

public static class GameState
{
    public static GamePhase Phase { get; set; } = GamePhase.None;
    public static int CurrentScore { get; set; }
    public static float PlayerCurrentScale { get; set; } = 2f;
    public static RYBColor CurrentRYBColor { get; set; } = RYBColor.white;
    public static Color CurrentDisplayColor { get; set; } = Color.white;

    public static void Reset()
    {
        Phase = GamePhase.None;
        CurrentScore = 0;
        PlayerCurrentScale = 2f;
        CurrentRYBColor = RYBColor.white;
        CurrentDisplayColor = Color.white;
    }
}
