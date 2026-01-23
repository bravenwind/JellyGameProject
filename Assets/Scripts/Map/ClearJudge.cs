using Unity.VisualScripting;
using UnityEngine;

public class ClearJudge : MonoBehaviour
{
    public QuestStarUI questStarUI;
    public GameTimer gameTimer;
    public UIManager uiManager;
    public UIPoolManager uIPoolManager;

    public float halfLength = 6.0f;
    public LayerMask playerLayerMask;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.O))
        {
            DataManager.Instance.currentColor = DataManager.Instance.thisGameRangeRule.color;
            DataManager.Instance.playerCurrentScaleLevel = DataManager.Instance.targetScaleLevel;
        }

        Collider[] cols = Physics.OverlapBox(transform.position, Vector3.one * halfLength, Quaternion.identity, playerLayerMask);
        if (cols.Length == 0 )
        {
            return;
        }

        if (DataManager.Instance.DetermineCurrentColor(DataManager.Instance.currentColor) == DataManager.Instance.thisGameRangeRule.resultType
           && DataManager.Instance.playerCurrentScaleLevel == DataManager.Instance.targetScaleLevel)
        {
            questStarUI.ApplySuccess(2);
            if (gameTimer.limitTime - gameTimer.currentTime <= DataManager.Instance.targetTime)
            {
                 questStarUI.ApplySuccess(3);
            }

            uiManager.SetState(UIState.GameOver);
            uIPoolManager.DisableParent();
            gameObject.SetActive(false);
        }

    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(transform.position, Vector3.one * halfLength);
    }
}
