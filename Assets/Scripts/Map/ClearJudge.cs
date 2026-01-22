using Unity.VisualScripting;
using UnityEngine;

public class ClearJudge : MonoBehaviour
{
    public QuestStarUI questStarUI;
    public GameTimer gameTimer;
    public UIManager uiManager;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            if (DataManager.Instance.DetermineCurrentColor(DataManager.Instance.currentColor) == JellyColorType.Blue
                && DataManager.Instance.playerCurrentScaleLevel == DataManager.Instance.targetScaleLevel)
            {
                questStarUI.ApplySuccess(2);
                if (gameTimer.currentTime >= DataManager.Instance.targetTime)
                {
                    questStarUI.ApplySuccess(3);
                }

                uiManager.SetState(UIState.GameOver);
                PlayFXAudio.Instance.PlayMissionCompleteSound();
            }
        }
    }
}
