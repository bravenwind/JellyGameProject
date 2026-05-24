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

    public void SetScoreFromScale(float scale)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        _currentScore = DataManager.Instance.ScoreFromScale(scale);
        UpdateBotData($"AI 봇 {photonView.ViewID}", _currentScore, scale);
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

    /// <summary>
    /// 봇의 현재 색상을 룸 프로퍼티에 저장. 씬 전환 후에도 색상 복원 가능.
    /// MasterClient에서만 실행. 게임 종료 직전에 호출.
    /// </summary>
    public void SyncColor(Color c)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        if (string.IsNullOrEmpty(_botPrefix)) return;
        Hashtable props = new Hashtable
        {
            { $"{_botPrefix}_Color_R", c.r },
            { $"{_botPrefix}_Color_G", c.g },
            { $"{_botPrefix}_Color_B", c.b }
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

    /// <summary>
    /// 봇의 리더보드 속성 제거. 게임오버 처리 시 호출 (오브젝트는 살아있음).
    /// MasterClient만 실행.
    /// </summary>
    public void ClearBotProperties()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        if (string.IsNullOrEmpty(_botPrefix)) return;

        Hashtable props = new Hashtable
        {
            { $"{_botPrefix}_Name", null },
            { $"{_botPrefix}_Score", null },
            { $"{_botPrefix}_Scale", null },
            { $"{_botPrefix}_Color_R", null },
            { $"{_botPrefix}_Color_G", null },
            { $"{_botPrefix}_Color_B", null }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void OnDestroy()
    {
        ClearBotProperties();
    }
}