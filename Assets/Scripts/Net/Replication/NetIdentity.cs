using System;
using UnityEngine;

namespace JellyNet
{
    public class NetIdentity : MonoBehaviour
    {
        public int NetId { get; private set; }
        public int OwnerId { get; private set; }
        public int PrefabId { get; private set; }
        public bool IsBot { get; private set; }

        [SerializeField] private int sceneNetId;

        private LanPlayerState playerState;
        private LanPlayerVisual visual;
        private LanBotState botState;
        private AIPlayerMovement bot;

        public int SceneNetId { get { return sceneNetId; } set { sceneNetId = value; } }

        public void Assign(int netId, int ownerId, int prefabId)
        {
            NetId = netId;
            OwnerId = ownerId;
            PrefabId = prefabId;
        }

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
                if (NetManager.Offline)
                    return true;

                return IsMine;
            }
        }

        public bool IsSimulatedHere
        {
            get
            {
                if (NetManager.Offline)
                    return true;

                if (OwnerId == 0)
                    return NetManager.Instance.IsHost;

                return IsMine;
            }
        }

        public LanPlayerState PlayerState { get { return playerState; } }

        public LanPlayerVisual Visual { get { return visual; } }

        public LanBotState BotState { get { return botState; } }

        public AIPlayerMovement Bot { get { return bot; } }

        private void Awake()
        {
            playerState = GetComponent<LanPlayerState>();
            visual = GetComponent<LanPlayerVisual>();
            botState = GetComponent<LanBotState>();
            bot = GetComponent<AIPlayerMovement>();

            IsBot = bot != null;
        }

        public void RefreshComponentCache()
        {
            if (playerState == null)
                playerState = GetComponent<LanPlayerState>();
            if (visual == null)
                visual = GetComponent<LanPlayerVisual>();
            if (botState == null)
                botState = GetComponent<LanBotState>();
            if (bot == null)
            {
                bot = GetComponent<AIPlayerMovement>();
                IsBot = bot != null;
            }
        }
    }
}
