// ============================================================
// LeaderboardEntry.cs
// ============================================================
// 역할: 순위표의 한 줄 항목 표시
//       GameModeManager.UpdateLeaderboard()에서 생성 및 설정
// ============================================================

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LeaderboardEntry : MonoBehaviour
{
    [Header("UI 연결")]
    public TextMeshProUGUI rankText;       // "1"
    public TextMeshProUGUI nameText;       // "Player123"
    public TextMeshProUGUI scoreText;      // "1500"
    public Image backgroundImage;          // 내 항목은 하이라이트

    [Header("색상 설정")]
    public Color myEntryColor = new Color(1f, 1f, 0f, 0.3f);   // 내 항목: 노란 배경
    public Color botEntryColor = new Color(0.5f, 0.5f, 0.5f, 0.2f); // AI: 회색

    /// <summary>
    /// GameModeManager에서 호출해서 항목 초기화
    /// </summary>
    public void Setup(int rank, string playerName, int score, bool isMe)
    {
        if (rankText != null) rankText.text = rank.ToString();
        if (nameText != null) nameText.text = playerName;
        if (scoreText != null) scoreText.text = score.ToString("N0"); // 1,500 형식

        // 내 항목이면 하이라이트
        if (backgroundImage != null)
        {
            backgroundImage.color = isMe ? myEntryColor : Color.clear;
        }

        // 1위는 금색
        if (rank == 1 && rankText != null)
        {
            rankText.color = new Color(1f, 0.84f, 0f); // 금색
        }
    }
}
