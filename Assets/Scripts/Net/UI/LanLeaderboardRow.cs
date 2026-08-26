//순위표의 한 줄. LanLeaderboardUI가 생성하고 설정한다.

using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JellyNet
{
        public class LanLeaderboardRow : MonoBehaviour
        {
        [Header("UI 연결")]
        [SerializeField] private TextMeshProUGUI rankText;       // "1"
        [SerializeField] private TextMeshProUGUI nameText;       // "Player123"
        [SerializeField] private TextMeshProUGUI scoreText;      // "1500"
        [SerializeField] private Image backgroundImage;          // 내 항목은 하이라이트

        [Header("색상 설정")]
        [SerializeField] private Color myEntryColor = new Color(1f, 1f, 0f, 0.3f);   // 내 항목: 노란 배경

        /// <summary>
        /// LanLeaderboardUI가 호출해서 항목 초기화
        /// </summary>
        public void Setup(int rank, string playerName, int score, bool isMe, Color playerColor = default)
        {
            if (rankText != null)
                rankText.text = rank.ToString();
            if (nameText != null)
            {
                nameText.text = playerName;
                nameText.color = playerColor.a > 0.01f ? playerColor : Color.white;
            }
            if (scoreText != null)
                scoreText.text = score.ToString("N0"); // 1,500 형식

            // 내 항목이면 하이라이트
            if (backgroundImage != null)
                backgroundImage.color = isMe ? myEntryColor : Color.clear;

            // 1위는 금색, 그 외는 흰색으로 복원
            if (rankText != null)
                rankText.color = rank == 1 ? new Color(1f, 0.84f, 0f) : Color.white;
        }
    }
}
