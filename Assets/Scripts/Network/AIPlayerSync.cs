using UnityEngine;
using Photon.Pun;
using ExitGames.Client.Photon;

// [LAN 이식] RequireComponent(PhotonView) 제거 — 프리팹에서 PhotonView를 걷어내기 위함.
public class AIPlayerSync : MonoBehaviourPun, IPunInstantiateMagicCallback
{
    private string _botPrefix;

    private int _currentScore = 0;
    public int CurrentScore => _currentScore;

    public string BotPrefix => _botPrefix;

    // IPunInstantiateMagicCallback: PhotonNetwork.Instantiate/InstantiateRoomObject 완료 후 호출.
    // Start()보다 먼저 실행되며 이 시점에는 photonView.ViewID가 반드시 유효하다.
    // Start()에서 ViewID를 읽으면 PUN2 내부 초기화 타이밍에 따라 0이 반환되는 경우가 있어
    // 일부 봇의 이름표가 "AI 봇 0"으로 설정되거나 누락되는 버그가 발생한다.
    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        InitBotIdentity();
    }

    private void Start()
    {
        // OnPhotonInstantiate가 먼저 실행되지 않는 경로(씬 직접 배치 등)의 폴백
        if (string.IsNullOrEmpty(_botPrefix))
            InitBotIdentity();
    }

    private void InitBotIdentity()
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
        if (GameState.Phase == GamePhase.Result) return;
        ClearBotProperties();
    }
}