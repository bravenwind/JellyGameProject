using Unity.VisualScripting;
using UnityEngine;

public class ClearJudge : MonoBehaviour
{
    public ResultStarsUI resultStarsUI;
    public GameTimer gameTimer;
    public UIManager uiManager;
    public UIPoolManager uIPoolManager;
    public PlayerController playerController;

    public float halfLength = 6.0f;
    public LayerMask playerLayerMask;

    // ★ 중복 실행 방지용 플래그 변수
    private bool isCleared = false;

    [Header("저울 설정")]
    [Tooltip("밟았을 때 아래로 내려갈 거리")]
    public float sinkDistance = 1.0f;

    [Tooltip("내려가고 올라오는 속도 (낮을수록 더 서서히 움직임)")]
    public float moveSpeed = 1.5f;

    public Transform scaleTransform;

    private Vector3 _originalPos;
    private Vector3 _pressedPos;
    private Vector3 _targetPos;

    // 저울 위에 올라가 있는 물체의 개수
    private int _objectsOnScale = 0;

    void Start()
    {
        _originalPos = scaleTransform.position;
        _pressedPos = _originalPos + Vector3.down * sinkDistance;
        _targetPos = _originalPos;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("PlayerMesh"))
        {
            _objectsOnScale++;
            _targetPos = _pressedPos;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("PlayerMesh"))
        {
            _objectsOnScale--;

            if (_objectsOnScale <= 0)
            {
                _objectsOnScale = 0;
                _targetPos = _originalPos;
            }
        }
    }

    private void Update()
    {
        // ★ 클리어 이후에는 저울도 멈추고(고정) 더 이상 로직을 실행하지 않음
        if (isCleared) return;

        if (Input.GetKeyDown(KeyCode.O))
        {
            DataManager.Instance.currentColor = DataManager.Instance.thisGameRangeRule.color;
            DataManager.Instance.playerCurrentScaleLevel = DataManager.Instance.targetScaleLevel;
        }

        // 1. 저울 서서히 이동
        scaleTransform.position = Vector3.MoveTowards(scaleTransform.position, _targetPos, moveSpeed * Time.deltaTime);


        // 수정 후 (약 0.01f 이내로 들어오면 도착한 것으로 간주)
        if (Vector3.Distance(scaleTransform.position, _pressedPos) < 0.01f && _objectsOnScale > 0)
        {
            Debug.Log("저울 눌림");
            JudgeClear();
        }
    }

    private void JudgeClear()
    {
        if (DataManager.Instance.DetermineCurrentColor(DataManager.Instance.currentColor) == DataManager.Instance.thisGameRangeRule.resultType
   && DataManager.Instance.playerCurrentScaleLevel == DataManager.Instance.targetScaleLevel)
        {
            isCleared = true;
            
            if (gameTimer.limitTime - gameTimer.currentTime <= DataManager.Instance.targetTime)
            {
                resultStarsUI.SetStarIndex(3);
            }
            else
            {
                resultStarsUI.SetStarIndex(2);
            }

            PlaySFXAudio.Instance.StopWalking();
            playerController.enabled = false;
            PlaySFXAudio.Instance.PlayMissionCompleteSound();

            uiManager.SetState(UIState.GameOver);
            uIPoolManager.DisableParent();
        }
    }
}