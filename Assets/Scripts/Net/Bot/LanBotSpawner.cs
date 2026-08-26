using UnityEngine;
using UnityEngine.AI;

namespace JellyNet
{
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

        private bool spawned;

        private void Awake()
        {
            Instance = this;

            if (LanRoomConfig.HasValue)
            {
                botCount = LanRoomConfig.AiCount;
                if (verboseLog)
                    Debug.Log("[봇] 로비 설정에 따라 " + botCount + "마리로 맞춥니다.");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private bool warned;

        private void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;

            LanGameFlow flow = LanGameFlow.Instance;
            if (flow == null)
            {
                WarnOnce("LanGameFlow가 씬에 없습니다 — 게임 시작 시점을 알 수 없어 봇을 못 뿌립니다.");
                return;
            }

            //Playing까지 기다리면 카운트다운 동안 봇이 없어서 순위표에 사람만 뜬다.
            //카운트다운이 시작되는 순간 뿌려두면 시작 전에 상대를 눈으로 확인할 수 있고,
            //봇은 Phase != Playing 동안 스스로 멈춰 있으므로 미리 나와도 움직이지 않는다
            if ((flow.Phase == GamePhase.Countdown || flow.Phase == GamePhase.Playing) && !spawned)
            {
                spawned = true;
                SpawnAll();
            }
            else if (flow.Phase == GamePhase.Loading)
                spawned = false;
        }

        private void WarnOnce(string msg)
        {
            if (warned)
                return;
            warned = true;
            Debug.LogWarning("[봇] " + msg);
        }

        private void SpawnAll()
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

            if (verboseLog)
                Debug.Log("[봇] " + botCount + "마리 스폰 완료");
        }

        private static int FindBotPrefab(GameObject[] prefabs)
        {
            if (prefabs == null)
                return -1;
            for (int i = 1; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null)
                    continue;
                if (prefabs[i].GetComponentInChildren<AIPlayerMovement>(true) != null)
                    return i;
            }
            return -1;
        }

        private void SpawnOne()
        {
            Vector3 pos = PickPos();

            NetIdentity spawned = NetWorld.Instance.HostSpawn(botPrefabId, NetHost.HOST_ID, pos);
            if (spawned == null)
                return;

            NavMeshAgent agent = spawned.GetComponentInChildren<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = false;
                agent.transform.position = pos;
                agent.enabled = true;
                if (agent.isOnNavMesh)
                    agent.Warp(pos);
            }

            if (verboseLog)
                Debug.Log("[봇] net" + spawned.NetId + " 스폰 @ " + pos);
        }

        private Vector3 PickPos()
        {
            if (LanSpawnPoints.Instance != null)
            {
                Vector3 p = LanSpawnPoints.Instance.Take();
                if (p != Vector3.zero)
                    return Snap(p);
            }

            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices != null && tri.vertices.Length > 0)
                return Snap(tri.vertices[Random.Range(0, tri.vertices.Length)]);

            return Vector3.zero;
        }

        private static Vector3 Snap(Vector3 p)
        {
            NavMeshHit hit;
            //봇은 타입 0(PlayerJelly)이라 int 오버로드로 충분하다. 영역만 Walkable로 좁힌다
            return NavMesh.SamplePosition(p, out hit, 10f, NavMeshUtil.WalkableMask) ? hit.position : p;
        }
    }
}
