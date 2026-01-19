using TMPro;
using UnityEngine;

public class ScoreUI : MonoBehaviour
{
    public TMP_Text scoreText;

    private void Start()
    {
        ChangeScoreUI();
    }

    public void ChangeScoreUI()
    {
        scoreText.text = DataManager.Instance.currentScore.ToString() + "Á¡";
    }
}
