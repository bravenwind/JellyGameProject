using UnityEngine;
using TMPro;

public class GameTimer : MonoBehaviour
{
    public float limitTime = 300f;
    public TextMeshProUGUI timerText;

    private float currentTime;
    private int lastSecond = -1; // 마지막으로 표시한 '초'를 저장 (초기값은 불가능한 값으로)

    void Start()
    {
        currentTime = limitTime;
    }

    void Update()
    {
        if (currentTime <= 0) return;

        currentTime -= Time.deltaTime;
        if (currentTime < 0) currentTime = 0;

        // 현재 시간을 정수(초)로 변환
        int currentSecond = Mathf.FloorToInt(currentTime);

        // ★ 핵심 최적화: "지난번 표시한 시간과 다를 때만" 텍스트 갱신
        if (currentSecond != lastSecond)
        {
            UpdateTimerText(currentSecond);
            lastSecond = currentSecond; // 갱신했음을 기록
        }
    }

    // 매개변수로 현재 초를 받아서 처리
    void UpdateTimerText(int secondsLeft)
    {
        int minutes = secondsLeft / 60;
        int seconds = secondsLeft % 60;

        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}