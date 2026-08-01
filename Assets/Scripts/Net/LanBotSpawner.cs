using UnityEngine;
using UnityEngine.AI;

namespace JellyNet
{
    /// <summary>
    /// AI 봇을 호스트가 스폰한다. NetworkManager.SpawnBots()를 옮긴 것.
    ///
    /// ★ 왜 호스트만 스폰하는가
    ///   봇은 '네트워크 오브젝트'다. 각자 만들면 같은 봇이 화면마다 따로 생겨
    ///   서로 다른 곳으로 걸어간다. 호스트가 하나 만들고 SpawnEntity로 복제하면
    ///   전원이 같은 봇 하나를 보게 된다. 젤리와 완전히 같은 구조다.
    ///
    /// ★ 언제 스폰하는가
    ///   원본은 게임 시작(카운트다운 직전)에 뿌렸다. 그대로 따른다.
    ///   대기 중에 미리 뿌리면 늦게 들어온 사람 화면에서 봇이 이미 흩어져 있어
    ///   "다 같이 시작"이 깨진다.
    /// </summary>
    public class LanBotSpawner : MonoBehaviour
    {
        public static LanBotSpawner Instance { get; private set; }

        [Header("봇")]
        [Tooltip("NetWorld.prefabs 배열에서 봇 프리팹의 인덱스. 0(플레이어)이면 스폰하지 않는다.")]
        public int botPrefabId = 0;

        [Tooltip("몇 마리를 뿌릴지.")]
        public int botCount = 3;

        [Header("진단")]
        public bool verboseLog = true;

        bool _spawned;

        void Awake()
        {
            Instance = this;

            // 로비에서 정한 AI 수가 있으면 그걸 따른다.
            // (씬을 직접 열어 테스트할 때는 인스펙터 값을 그대로 쓴다)
            if (LanRoomConfig.HasValue)
            {
                botCount = LanRoomConfig.AiCount;
                if (verboseLog) Debug.Log("[봇] 로비 설정에 따라 " + botCount + "마리로 맞춥니다.");
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ★ 조용한 실패를 없앤다.
        //   스포너가 안 돌 이유는 여러 개인데(호스트가 아님·프리팹 미등록·인덱스 0),
        //   전부 return이라 콘솔에 아무것도 안 남으면 원인을 못 찾는다.
        //   실제로 "씬에 컴포넌트가 없다"는 이유로 한참을 헤맸다.
        bool _warned;

        void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost) return;

            LanGameFlow flow = LanGameFlow.Instance;
            if (flow == null)
            {
                WarnOnce("LanGameFlow가 씬에 없습니다 — 게임 시작 시점을 알 수 없어 봇을 못 뿌립니다.");
                return;
            }

            // 게임이 시작되는 순간 한 번만
            if (flow.Phase == GamePhase.Playing && !_spawned)
            {
                _spawned = true;
                SpawnAll();
            }
            else if (flow.Phase == GamePhase.Loading)
            {
                _spawned = false;   // 다음 판을 위해 되돌린다
            }
        }

        void WarnOnce(string msg)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[봇] " + msg);
        }

        void SpawnAll()
        {
            if (botCount <= 0)
            {
                Debug.Log("[봇] botCount가 " + botCount + " 이라 뿌리지 않습니다.");
                return;
            }

            if (NetWorld.Instance == null)
            {
                Debug.LogWarning("[봇] NetWorld가 없습니다.");
                return;
            }

            GameObject[] prefabs = NetWorld.Instance.prefabs;

            if (botPrefabId <= 0)
            {
                // ★ 자동 탐색 — 인덱스를 손으로 적는 걸 잊어도 동작하게.
                //   0번은 플레이어라 botPrefabId가 0이면 '미지정'과 구분되지 않는다.
                int found = FindBotPrefab(prefabs);
                if (found < 0)
                {
                    Debug.LogWarning("[봇] NetWorld.prefabs에서 봇 프리팹(AIPlayerMovement 보유)을 "
                                     + "찾지 못했습니다. 배열 맨 뒤에 AIPlayer 프리팹을 추가해주세요.");
                    return;
                }
                botPrefabId = found;
                Debug.Log("[봇] 봇 프리팹을 prefabs[" + found + "] 에서 자동으로 찾았습니다.");
            }

            if (prefabs == null || botPrefabId >= prefabs.Length || prefabs[botPrefabId] == null)
            {
                Debug.LogWarning("[봇] prefabs[" + botPrefabId + "] 가 비어 있습니다.");
                return;
            }

            if (prefabs[botPrefabId].GetComponentInChildren<AIPlayerMovement>(true) == null)
            {
                Debug.LogWarning("[봇] prefabs[" + botPrefabId + "] (" + prefabs[botPrefabId].name
                                 + ") 에 AIPlayerMovement가 없습니다. "
                                 + "Tools ▸ LAN 이식 ▸ ⑨ 를 먼저 실행해주세요.");
                return;
            }

            for (int i = 0; i < botCount; i++) SpawnOne();

            if (verboseLog) Debug.Log("[봇] " + botCount + "마리 스폰 완료");
        }

        static int FindBotPrefab(GameObject[] prefabs)
        {
            if (prefabs == null) return -1;
            for (int i = 1; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null) continue;
                if (prefabs[i].GetComponentInChildren<AIPlayerMovement>(true) != null) return i;
            }
            return -1;
        }

        void SpawnOne()
        {
            Vector3 pos = PickPos();

            // 봇의 주인은 호스트다. 그래야 NetTransform이 호스트에서만 위치를 보내고
            // 나머지는 받기만 한다(= 원본에서 마스터가 봇을 굴리던 것과 같다).
            NetIdentity spawned = NetWorld.Instance.HostSpawn(botPrefabId, NetHost.HostId, pos);
            if (spawned == null) return;

            // ★ NavMeshAgent 안착 — 젤리와 같은 이유, 같은 순서.
            //   Instantiate 위치가 NavMesh에서 조금만 벗어나도 Unity가 agent를 꺼버리고,
            //   그러면 AIPlayerMovement.InitAndRun이 5초를 헤매다 포기한다.
            NavMeshAgent ag = spawned.GetComponentInChildren<NavMeshAgent>();
            if (ag != null)
            {
                ag.enabled = false;
                ag.transform.position = pos;
                ag.enabled = true;
                if (ag.isOnNavMesh) ag.Warp(pos);
            }

            if (verboseLog) Debug.Log("[봇] net" + spawned.NetId + " 스폰 @ " + pos);
        }

        /// <summary>
        /// 스폰 위치. 플레이어 스폰 슬롯을 함께 쓰되, 남은 슬롯을 이어서 가져간다.
        /// 슬롯이 없으면 NavMesh에서 직접 뽑는다.
        /// </summary>
        Vector3 PickPos()
        {
            if (LanSpawnPoints.Instance != null)
            {
                Vector3 p = LanSpawnPoints.Instance.Take();
                if (p != Vector3.zero) return Snap(p);
            }

            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices != null && tri.vertices.Length > 0)
                return Snap(tri.vertices[Random.Range(0, tri.vertices.Length)]);

            return Vector3.zero;
        }

        static Vector3 Snap(Vector3 p)
        {
            NavMeshHit hit;
            return NavMesh.SamplePosition(p, out hit, 10f, NavMesh.AllAreas) ? hit.position : p;
        }
    }
}
