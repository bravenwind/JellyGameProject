using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.InputSystem.LowLevel;

public class GameTimer : MonoBehaviour
{
    public float limitTime = 300f;
    public TextMeshProUGUI timerText;
    public ResultStarsUI resultStarsUI;
    public UIManager uiManager;
    public SoftBody3D softBody3D;
    public PlayerMovement playerController;

    public Animator playerAnimController;
    public MainCamera_Action mainCamera_Action;

    public float currentTime;
    private int lastSecond = -1;

    // ★ 게임이 이미 종료되었는지 체크하는 변수 추가
    private bool isGameEnded = false;

    void Start()
    {
        currentTime = limitTime;
        // 게임 시작 시 타임 스케일 정상화 (재시작 대비)
        Time.timeScale = 1f;
    }

    void Update()
    {
        // ★ 게임이 종료되었다면 더 이상 타이머 코드를 실행하지 않음
        if (isGameEnded) return;

        if (currentTime <= 0)
        {
            GameFail();
            return; // GameFail() 실행 후 아래 코드 실행 방지
        }

        currentTime -= Time.deltaTime;
        if (currentTime < 0) currentTime = 0;

        int currentSecond = Mathf.FloorToInt(currentTime);

        if (currentSecond != lastSecond)
        {
            UpdateTimerText(currentSecond);
            lastSecond = currentSecond;
        }
    }

    void UpdateTimerText(int secondsLeft)
    {
        int minutes = secondsLeft / 60;
        int seconds = secondsLeft % 60;

        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    public void GameFail()
    {
        if (isGameEnded) return; // 중복 실행 방지
        isGameEnded = true;

        // 3. ★ 게임 내 모든 시간(물리, Update 등) 정지
        Time.timeScale = 0f;

        PlaySFXAudio.Instance.StopWalking();

        playerController.enabled = false;

        //playerCloth.enabled = false;
        softBody3D.DisableCloth();

        // 1. 플레이어 애니메이터가 멈춘 시간에도 동작하도록 설정
        playerAnimController.updateMode = AnimatorUpdateMode.UnscaledTime;

        mainCamera_Action.GameFailSizeChange();
        // 2. 애니메이션 실행
        resultStarsUI.SetStarIndex(0);
        playerAnimController.SetTrigger("GameFail");
        PlaySFXAudio.Instance.PlayFailSound();

        // 주의: gameObject.SetActive(false)를 지워야 합니다. 
        // 타이머 오브젝트가 꺼지면 아래의 OnFailAnimationFinished 함수도 호출될 수 없습니다.
    }
}