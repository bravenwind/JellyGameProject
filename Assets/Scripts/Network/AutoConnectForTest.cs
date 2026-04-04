// ============================================================
// AutoConnectForTest.cs
// ============================================================
// 역할: 에디터에서 게임 씬을 직접 Play할 때 자동으로 Photon 연결
//       타이틀 씬 없이 바로 테스트할 때만 사용
//       실제 빌드/배포 시에는 이 컴포넌트 비활성화 또는 제거
// ============================================================

using UnityEngine;
using Photon.Pun;

public class AutoConnectForTest : MonoBehaviour
{
    [Tooltip("테스트용 닉네임")]
    public string testPlayerName = "TestPlayer";

    private void Start()
    {
        // 이미 방에 있으면 패스 (타이틀 씬에서 정상 흐름으로 들어온 경우)
        if (PhotonNetwork.InRoom)
        {
            Debug.Log("[AutoConnect] 이미 방에 있음 → 자동 연결 스킵");
            return;
        }

        Debug.Log("[AutoConnect] 테스트 자동 연결 시작...");
        NetworkManager.Instance?.StartConnect(testPlayerName);
    }
}
