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

        // ─────────────────────────────────────────────────────────
        //  캐릭터 컴포넌트 캐시
        // ─────────────────────────────────────────────────────────
        //
        // ★ 왜 여기에 모으나
        //   메시지 하나를 처리할 때마다 id.GetComponent<LanPlayerState>() 같은 조회가
        //   코드 46곳에 흩어져 있었다. netId로 오브젝트를 찾은 직후 거의 항상
        //   "그럼 그 안의 무엇"을 다시 찾는데, 그 답은 스폰 순간에 이미 정해져 있다.
        //   여기서 한 번만 찾아두면 호출부는 점 하나로 끝난다.
        //
        //   젤리·씬 사탕은 이 컴포넌트들이 없으므로 전부 null이다. 그래서
        //   호출부의 null 검사는 그대로 유효하고, "캐릭터인가"의 판정도 겸한다.
        [NonSerialized] private LanPlayerState playerState;
        [NonSerialized] private LanPlayerVisual visual;
        [NonSerialized] private LanBotState botState;
        [NonSerialized] private AIPlayerMovement bot;

        /// <summary>사람 캐릭터의 네트워크 상태. 봇·젤리면 null.</summary>
        public LanPlayerState PlayerState { get { return playerState; } }

        /// <summary>사람·봇의 표현(크기·색·애니메이션). 젤리면 null.</summary>
        public LanPlayerVisual Visual { get { return visual; } }

        /// <summary>봇의 네트워크 상태. 사람·젤리면 null.</summary>
        public LanBotState BotState { get { return botState; } }

        /// <summary>봇의 두뇌. 사람·젤리면 null.</summary>
        public AIPlayerMovement Bot { get { return bot; } }

        private void Awake()
        {
            playerState = GetComponent<LanPlayerState>();
            visual = GetComponent<LanPlayerVisual>();
            botState = GetComponent<LanBotState>();
            bot = GetComponent<AIPlayerMovement>();

            IsBot = bot != null;
        }

        //NetWorld.EnsurePlayerComponents가 스폰 도중 AddComponent로 붙이는 경우가 있다.
        //Awake는 그보다 먼저 돌았으므로 캐시가 비어 있다 — 그때 다시 잡아준다
        public void RefreshComponentCache()
        {
            if (playerState == null) playerState = GetComponent<LanPlayerState>();
            if (visual == null) visual = GetComponent<LanPlayerVisual>();
            if (botState == null) botState = GetComponent<LanBotState>();
            if (bot == null) { bot = GetComponent<AIPlayerMovement>(); IsBot = bot != null; }
        }
    }
}
