using UnityEngine;
using Photon.Pun;
using ExitGames.Client.Photon;

[RequireComponent(typeof(PhotonView))]
public class AIPlayerSync : MonoBehaviourPun
{
    private string _botPrefix;

    // 💡 봇의 현재 점수를 기억할 변수 추가
    private int _currentScore = 0;

    private void Start()
    {
        // 봇 이름은 ViewID로 결정적(deterministic)이므로 모든 클라이언트에서 동일하게 계산 가능
        string botName = $"AI 봇 {photonView.ViewID}";

        // ── 이름표 설정 (모든 클라이언트에서 실행) ──
        NameTagBillboard nameTag = GetComponentInChildren<NameTagBillboard>(true);
        if (nameTag != null)
        {
            nameTag.SetName(botName);
            nameTag.ApplyRoleColor(NameTagRole.Bot);
        }

        // ── 이하 MasterClient 전용 처리 ──
        if (!PhotonNetwork.IsMasterClient) return;

        _botPrefix = $"Bot{photonView.ViewID}";
        _currentScore = 0;

        UpdateBotData(botName, _currentScore);
        gameObject.name = gameObject.name + "_" + botName;
    }

    // 💡 외부(JellyColliderAbsorb 등)에서 점수를 올릴 때 호출할 함수 새로 추가
    public void AddScore(int amount)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 점수 누적
        _currentScore += amount;

        // 누적된 점수로 방 속성 업데이트
        UpdateBotData($"AI 봇 {photonView.ViewID}", _currentScore);
    }

    public void UpdateBotData(string botName, int score)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        Hashtable props = new Hashtable
        {
            { $"{_botPrefix}_Name", botName },
            { $"{_botPrefix}_Score", score }
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void OnDestroy()
    {
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom && !string.IsNullOrEmpty(_botPrefix))
        {
            Hashtable props = new Hashtable
            {
                { $"{_botPrefix}_Name", null },
                { $"{_botPrefix}_Score", null }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
    }
}