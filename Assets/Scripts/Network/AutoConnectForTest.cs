using UnityEngine;
using Photon.Pun;

[DefaultExecutionOrder(-10)]
public class AutoConnectForTest : MonoBehaviour
{
    [Tooltip("테스트용 닉네임")]
    public string testPlayerName = "TestPlayer";

    [SerializeField]
    private GameModeType testGameMode = GameModeType.Push;

    private void Start()
    {
        // 빌드 시에는 Start 내부 로직만 텅 비게 됨
#if UNITY_EDITOR
        // 이미 방에 있으면 패스 (타이틀 씬에서 정상 흐름으로 들어온 경우)
        if (PhotonNetwork.InRoom)
        {
            Debug.Log("[AutoConnect] 이미 방에 있음 → 자동 연결 스킵");
            return;
        }

        Debug.Log($"[AutoConnect] 테스트 자동 연결 시작... 모드={testGameMode}");
        GameState.CurrentGameMode = testGameMode;
        NetworkManager.SelectedGameMode = testGameMode;
        NetworkManager.Instance?.StartConnect(testPlayerName);
#endif
    }
}