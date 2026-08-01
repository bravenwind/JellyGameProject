using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 네트워크 오브젝트의 신분증. Photon의 PhotonView 자리를 대신한다.
    ///
    /// 네트워크로 복제되는 오브젝트(플레이어·젤리·봇)에는 반드시 이 컴포넌트가 붙는다.
    /// 그래야 "이건 몇 번 오브젝트고, 누구 것인가"를 양쪽이 같은 기준으로 알 수 있다.
    /// </summary>
    public class NetIdentity : MonoBehaviour
    {
        /// <summary>네트워크 전체에서 이 오브젝트를 가리키는 고유 번호. 호스트가 발급한다.</summary>
        public int NetId;

        /// <summary>
        /// 씬에 미리 배치된 오브젝트의 <b>고정 ID</b>. 0이면 런타임 스폰 오브젝트.
        ///
        /// ★ 왜 필요한가
        ///   씬에 깔아둔 젤리 수백 개는 호스트가 스폰한 게 아니라 양쪽이 각자 씬에서 로드한다.
        ///   그래서 "내 화면의 이 젤리 = 상대 화면의 저 젤리"를 이어줄 공통 번호가 필요하다.
        ///   에디터에서 미리 부여해두면 같은 씬을 로드한 모두가 같은 번호를 갖게 된다.
        ///   (Photon이 씬 배치 PhotonView에 고정 ViewID를 주는 것과 같은 원리)
        /// </summary>
        public int SceneNetId;

        /// <summary>이 오브젝트를 조종하는 사람. 호스트=1, 참가자=호스트가 준 번호.</summary>
        public int OwnerId;

        /// <summary>어떤 프리팹으로 만들어졌는지(NetWorld.prefabs 배열의 인덱스).</summary>
        public int PrefabId;

        /// <summary>
        /// 내가 조종하는 오브젝트인가? Photon의 photonView.IsMine에 해당.
        ///
        /// 입력을 받아 움직이고 위치를 전송하는 쪽은 소유자뿐이다.
        /// 나머지는 받은 위치를 따라가기만 한다.
        /// </summary>
        public bool IsMine
        {
            get
            {
                NetManager net = NetManager.Instance;
                return net != null && net.MyId != 0 && net.MyId == OwnerId;
            }
        }

        /// <summary>
        /// 네트워크가 꺼져 있으면(오프라인/단독 테스트) 무조건 내 것으로 본다.
        ///
        /// ★ 왜 따로 두는가
        ///   IsMine은 접속 중이 아닐 때 항상 false다. 그런데 AI 스크립트들은
        ///   "내 것이 아니면 NavMeshAgent를 끈다"로 동작하므로, 오프라인에서
        ///   그대로 쓰면 모든 AI가 얼어붙는다. AI의 구동 권한 판정에는 이쪽을 쓴다.
        /// </summary>
        /// <summary>
        /// 이 기계가 이 오브젝트의 AI·물리를 <b>직접 굴리는가</b>.
        ///
        /// ★ IsMine과 무엇이 다른가
        ///   씬에 미리 깔린 오브젝트는 주인이 없다(OwnerId = 0). 호스트가 스폰한 게
        ///   아니라 양쪽이 각자 씬에서 로드했기 때문이다.
        ///   그래서 IsMine을 그대로 쓰면 <b>호스트에서도 false</b>가 되어,
        ///   씬에 배치된 Wandering 젤리가 아무 데서도 안 움직인다.
        ///
        ///   주인이 없는 것은 호스트가 굴린다 — 이게 이 프로퍼티의 전부다.
        ///   접속이 없으면(오프라인) 전부 내가 굴린다.
        /// </summary>
        public bool IsSimulatedHere
        {
            get
            {
                NetManager net = NetManager.Instance;
                if (net == null || net.CurrentMode == NetManager.Mode.None) return true;
                if (OwnerId == 0) return net.IsHost;
                return IsMine;
            }
        }

        /// <summary>
        /// AI 봇인가.
        ///
        /// ★ 왜 필요한가
        ///   흡수 판정은 "젤리인가 아닌가"를 PrefabId >= 1로 구분해 왔다.
        ///   그런데 봇도 런타임 스폰 프리팹이라 그 조건에 걸려 젤리로 오인된다.
        ///   컴포넌트로 한 번만 판별해 캐시해 둔다(매 프레임 GetComponent를 피한다).
        /// </summary>
        [System.NonSerialized] public bool IsBot;

        void Awake()
        {
            IsBot = GetComponent<AIPlayerMovement>() != null;
        }

        /// <summary>
        /// 아무 컴포넌트에서든 그 개체의 네트워크 번호를 얻는다.
        /// photonView.ViewID를 쓰던 자리를 대체한다. 없으면 0.
        /// </summary>
        public static int IdOf(Component c)
        {
            if (c == null) return 0;
            NetIdentity id = c.GetComponentInParent<NetIdentity>();
            return id != null ? id.NetId : 0;
        }

        public bool IsMineOrOffline
        {
            get
            {
                NetManager net = NetManager.Instance;
                if (net == null || net.CurrentMode == NetManager.Mode.None) return true;
                return IsMine;
            }
        }
    }
}
