using TMPro;
using UnityEngine;

public class ScoreUI : MonoBehaviour
{
    public TMP_Text scoreText;

    private void OnEnable()
    {
        GameState.OnScoreChanged += OnScoreChanged;
        Refresh(GameState.CurrentScore);
    }

    private void OnDisable()
    {
        GameState.OnScoreChanged -= OnScoreChanged;
    }

    private void OnScoreChanged(int score) => Refresh(score);

    private void Refresh(int score)
    {
        scoreText.text = score.ToString();
    }
}
