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
    }
}
