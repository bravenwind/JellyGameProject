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

    // ★ 중복 실행 방지용 플래그 변수 추가
    private bool isCleared = false;

    private void Update()
    {
        // ★ 이미 클리어 판정이 났다면 더 이상 아래 코드를 실행하지 않음
        if (isCleared) return;

        if (Input.GetKeyDown(KeyCode.O))
        {
            DataManager.Instance.currentColor = DataManager.Instance.thisGameRangeRule.color;
            DataManager.Instance.playerCurrentScaleLevel = DataManager.Instance.targetScaleLevel;
        }

        Collider[] cols = Physics.OverlapBox(transform.position, Vector3.one * halfLength, Quaternion.identity, playerLayerMask);
        if (cols.Length == 0)
        {
            return;
        }

        if (DataManager.Instance.DetermineCurrentColor(DataManager.Instance.currentColor) == DataManager.Instance.thisGameRangeRule.resultType
           && DataManager.Instance.playerCurrentScaleLevel == DataManager.Instance.targetScaleLevel)
        {
            // ★ 조건을 만족하자마자 플래그를 true로 바꿔서 문을 잠금
            isCleared = true;

            questStarUI.ApplySuccess(2);
            if (gameTimer.limitTime - gameTimer.currentTime <= DataManager.Instance.targetTime)
            {
                questStarUI.ApplySuccess(3);
            }

            uiManager.SetState(UIState.GameSuccess);
            uIPoolManager.DisableParent();

            gameObject.SetActive(false);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(transform.position, Vector3.one * halfLength);
    }
}