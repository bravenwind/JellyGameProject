using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 온라인(Photon) 접속에 필요한 값들. 씬이 아니라 에셋 하나에 모아둔다.
    ///
    /// ★ App ID 를 코드에 박지 않는 이유
    ///   사람마다 다르고(각자 자기 대시보드의 앱을 쓴다) 빌드마다 바뀔 수 있는데,
    ///   코드에 있으면 바꿀 때마다 다시 컴파일해야 하고 무엇보다 "이 값이 어디서
    ///   오는지"를 코드를 읽어야만 알게 된다. 세팅은 세팅이 있는 곳에 둔다.
    ///
    /// ★ Photon 타입을 참조하지 않는 이유
    ///   AppSettings 를 그대로 담으면 이 파일이 PHOTON_REALTIME_5_OR_NEWER 안으로 들어가야 하고,
    ///   심볼을 끄는 순간 에셋이 "스크립트 없음"이 되어 적어둔 값이 날아간다.
    ///   문자열 셋만 들고 있다가 PhotonTransport 가 AppSettings 로 옮겨 담는다.
    /// </summary>
    [CreateAssetMenu(fileName = "PhotonAppSettings", menuName = "Jelly/Photon App Settings")]
    public class PhotonAppSettingsAsset : ScriptableObject
    {
        //Resources 아래 이 이름으로 두면 씬에 배선하지 않아도 읽힌다.
        //전송은 로비보다 먼저 살아나므로 인스펙터 참조를 걸어둘 자리가 마땅치 않다
        public const string RESOURCE_PATH = "PhotonAppSettings";

        [Tooltip("Photon 대시보드에서 만든 Realtime 앱의 App ID.")]
        [SerializeField] private string appIdRealtime = "";

        [Tooltip("비워두면 Photon 이 가장 빠른 지역을 고른다(첫 접속이 조금 느리다). "
               + "'kr' 처럼 못 박으면 그 지역만 쓴다 — 참가자가 전부 국내면 이쪽이 빠르다.")]
        [SerializeField] private string fixedRegion = "";

        [Tooltip("이 값이 다르면 같은 App ID 라도 서로의 방이 보이지 않는다. "
               + "옛 빌드와 방 목록이 섞이지 않게 하는 칸막이다.")]
        [SerializeField] private string appVersion = "1";

        public string AppIdRealtime { get { return appIdRealtime; } }
        public string FixedRegion { get { return fixedRegion; } }
        public string AppVersion { get { return appVersion; } }

        /// <summary>Resources 에서 읽는다. 없으면 null 을 돌려주고 이유를 남긴다.</summary>
        public static PhotonAppSettingsAsset Load()
        {
            PhotonAppSettingsAsset asset = Resources.Load<PhotonAppSettingsAsset>(RESOURCE_PATH);

            if (asset == null)
                Debug.LogError("[Photon] Resources/" + RESOURCE_PATH + " 이 없다. "
                             + "Assets/Create/Jelly/Photon App Settings 로 만들어 Resources 에 둘 것");
            else if (string.IsNullOrEmpty(asset.appIdRealtime))
                Debug.LogError("[Photon] " + RESOURCE_PATH + " 의 App ID 가 비어 있다");

            return asset;
        }
    }
}
