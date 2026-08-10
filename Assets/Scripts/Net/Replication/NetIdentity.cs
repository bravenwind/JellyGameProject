using System;
using UnityEngine;

namespace JellyNet
{
    public class NetIdentity : MonoBehaviour
    {
        public int NetId;

        public int SceneNetId;

        public int OwnerId;

        public int PrefabId;

        [NonSerialized] public bool IsBot;

        public bool IsMine
        {
            get
            {
                NetManager net = NetManager.Instance;
                return net != null && net.MyId != 0 && net.MyId == OwnerId;
            }
        }

        public bool IsMineOrOffline
        {
            get
            {
                NetManager net = NetManager.Instance;

                if (net == null || net.CurrentMode == NetManager.Mode.None)
                    return true;

                return IsMine;
            }
        }

        public bool IsSimulatedHere
        {
            get
            {
                NetManager net = NetManager.Instance;

                if (net == null || net.CurrentMode == NetManager.Mode.None)
                    return true;

                if (OwnerId == 0)
                    return net.IsHost;

                return IsMine;
            }
        }

        private void Awake()
        {
            IsBot = GetComponent<AIPlayerMovement>() != null;
        }

        public static int IdOf(Component component)
        {
            if (component == null)
                return 0;

            NetIdentity identity = component.GetComponentInParent<NetIdentity>();
            return identity != null ? identity.NetId : 0;
        }
    }
}
