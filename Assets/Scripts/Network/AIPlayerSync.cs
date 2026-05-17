using UnityEngine;
using Photon.Pun;
using ExitGames.Client.Photon;

[RequireComponent(typeof(PhotonView))]
public class AIPlayerSync : MonoBehaviourPun
{
    private string _botPrefix;

    private int _currentScore = 0;
    public int CurrentScore => _currentScore;

    public string BotPrefix => _botPrefix;

    private void Start()
    {
        string botName = $"AI 봇 {photonView.ViewID}";

        NameTagBillboard nameTag = GetComponentInChildren<NameTagBillboard>(true);
        if (nameTag != null)
        {
            nameTag.SetName(botName);
            nameTag.ApplyRoleColor(NameTagRole.Bot);
        }

        _botPrefix = $"Bot{photonView.ViewID}";

        if (!PhotonNetwork.IsMasterClient) return;

        _currentScore = 0;
        UpdateBotData(botName, _currentScore, transform.localScale.x);
        gameObject.name = gameObject.name + "_" + botName;
    }

    public void AddScore(int amount)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        _currentScore += amount;
        UpdateBotData($"AI 봇 {photonView.ViewID}", _currentScore, transform.localScale.x);
    }

    public void SyncScale(float scaleValue)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        Hashtable props = new Hashtable
        {
            { $"{_botPrefix}_Scale", scaleValue }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public float GetSyncedScale()
    {
        if (!PhotonNetwork.InRoom) return transform.localScale.x;
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        string key = $"{_botPrefix}_Scale";
        if (props.TryGetValue(key, out object val))
            return (float)val;
        return transform.localScale.x;
    }

    public void UpdateBotData(string botName, int score, float scale)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        Hashtable props = new Hashtable
        {
            { $"{_botPrefix}_Name", botName },
            { $"{_botPrefix}_Score", score },
            { $"{_botPrefix}_Scale", scale }
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
                { $"{_botPrefix}_Score", null },
                { $"{_botPrefix}_Scale", null }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
    }
}