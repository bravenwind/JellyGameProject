using System;
using UnityEngine;

namespace JellyNet
{
    public class NetIdentity : MonoBehaviour
    {
        // ★ 이 넷은 인스펙터 값이 아니라 NetWorld가 스폰하며 채우는 런타임 값이다.
        //   그래서 [SerializeField]가 아니라 프로퍼티다 — 밖에서 읽기만 하고,
        //   쓰는 것은 아래 HostAssign / 씬 ID 부여 경로로만 들어온다.
        public int NetId { get; private set; }
        public int OwnerId { get; private set; }
        public int PrefabId { get; private set; }
        public bool IsBot { get; private set; }

        // ★ 이 필드의 이름을 바꾸면 씬 배치물이 통째로 죽는다 — 한 번 겪었다
        //   씬은 배치된 오브젝트마다 이 값을 프리팹 오버라이드로 저장한다.
        //     - target: {fileID: …}
        //       propertyPath: sceneNetId      ← 필드 이름 문자열로 찾아간다
        //       value: 1000096
        //   이름을 바꾸자 244개가 전부 갈 곳을 잃어 값이 0이 됐고, RegisterSceneObjects가
        //   sceneNetId == 0 이면 건너뛰므로 씬 젤리가 하나도 네트워크에 등록되지 않았다.
        //   증상은 "미리 배치된 젤리만 흡수가 안 되고 제자리에 되살아남"이었다.
        //
        //   [FormerlySerializedAs]로는 못 고친다 — 그 특성은 컴포넌트가 직접 들고 있는
        //   값만 옮기고, <b>프리팹 오버라이드의 propertyPath 문자열은 건드리지 않는다.</b>
        //   결국 씬·프리팹 YAML의 propertyPath를 직접 새 이름으로 바꿔서 복구했다.
        //
        //씬에 미리 배치된 오브젝트에 에디터 도구가 찍어두는 고정 ID. 이것만 직렬화된다
        [SerializeField] private int sceneNetId;
        public int SceneNetId { get { return sceneNetId; } set { sceneNetId = value; } }

        /// <summary>스폰·씬 등록 시 신원을 확정한다. NetWorld와 씬 ID 부여 도구만 부른다.</summary>
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
