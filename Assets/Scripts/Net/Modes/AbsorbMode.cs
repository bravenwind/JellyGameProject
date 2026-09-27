using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JellyNet
{
    public class AbsorbMode : NetGameMode<AbsorbMode>
    {
        public bool spawnJelly = true;
        public float spawnInterval = 1.5f;
        public int maxJellyCount = 30;

        public float playerRadius = 0.5f;

        public float absorbSizeRatio = 1.15f;

        public float scoreRecomputeInterval = 0.25f;

        private readonly NetWriter w = new NetWriter();

        private readonly HashSet<int> runtimeJellies = new HashSet<int>();

        private float spawnTimer;
        private float scoreTimer;
        private NetIdentity myPlayer;

        private readonly List<NetIdentity> characters = new List<NetIdentity>();

        private List<int> jellyPrefabIds;

        private Vector3[] navVerts;

        private const int SPAWN_POS_TRIES = 8;

        private const float SPAWN_SAMPLE_RADIUS = 5f;

        protected override GameModeType Mode
        {
            get { return GameModeType.Absorb; }
        }

        protected override void ResetAll()
        {
            runtimeJellies.Clear();
            myPlayer = null;
            spawnTimer = 0f;
            scoreTimer = 0f;
        }

        protected override void HandleSpawned(NetIdentity id)
        {
            if (NetEntity.IsJelly(id))
            {
                if (id.NetId < NetConfig.SCENE_ID_BASE)
                    runtimeJellies.Add(id.NetId);
            }
            else if (id.IsMine && !id.IsBot)
                myPlayer = id;
        }

        protected override void HandleDespawned(int netId)
        {
            runtimeJellies.Remove(netId);
            if (myPlayer != null && myPlayer.NetId == netId)
                myPlayer = null;
        }

        private void Update()
        {
            if (IsOffline)
                return;

            if (IsHost && IsCurrentMode)
            {
                HostSpawnTick();
                HostScoreTick();
            }

            if (!IsPlaying)
                return;

            CheckMyPlayerAbsorb();
        }

        private void HostScoreTick()
        {
            scoreTimer += Time.deltaTime;
            if (scoreTimer < scoreRecomputeInterval)
                return;
            scoreTimer = 0f;

            NetEntity.CollectCharacters(characters);

            for (int i = 0; i < characters.Count; i++)
                NetEntity.HostSetScoreFromScale(characters[i]);
        }

        private void HostSpawnTick()
        {
            if (!spawnJelly || NetWorld.Instance == null)
                return;

            spawnTimer += Time.deltaTime;
            if (spawnTimer < spawnInterval)
                return;
            spawnTimer -= spawnInterval;

            if (runtimeJellies.Count >= maxJellyCount)
                return;

            GameObject[] prefabs = NetWorld.Instance.prefabs;
            if (prefabs == null || prefabs.Length <= NetConfig.JELLY_PREFAB_START)
            {
                spawnJelly = false;
                return;
            }

            int prefabId = PickJellyPrefab(prefabs);
            if (prefabId < 0)
                return;

            Vector3 pos;
            if (!TryPickJellySpawnPos(prefabs[prefabId], out pos))
                return;

            NetIdentity spawned = NetWorld.Instance.HostSpawn(prefabId, NetHost.HOST_ID, pos);

            if (spawned == null)
                return;

            NavMeshAgent ag =
                spawned.GetComponentInChildren<NavMeshAgent>();

            if (ag == null)
                return;

            ag.enabled = false;
            ag.transform.position = pos;
            ag.enabled = true;
        }

        private int PickJellyPrefab(GameObject[] prefabs)
        {
            if (jellyPrefabIds == null)
            {
                jellyPrefabIds = new List<int>();
                for (int i = NetConfig.JELLY_PREFAB_START; i < prefabs.Length; i++)
                {
                    GameObject p = prefabs[i];
                    if (p == null)
                        continue;
                    if (p.GetComponentInChildren<AIPlayerMovement>(true) != null)
                        continue;
                    if (p.GetComponentInChildren<PlayerMovement>(true) != null)
                        continue;
                    jellyPrefabIds.Add(i);
                }
            }

            if (jellyPrefabIds.Count == 0)
                return -1;
            return jellyPrefabIds[Random.Range(0, jellyPrefabIds.Count)];
        }

        private bool TryPickJellySpawnPos(GameObject prefab, out Vector3 pos)
        {
            NavMeshAgent agent = prefab != null
                ? prefab.GetComponentInChildren<NavMeshAgent>(true)
                : null;

            NavMeshQueryFilter filter = new NavMeshQueryFilter
            {
                agentTypeID = agent != null ? agent.agentTypeID : 0,
                areaMask = NavMeshUtil.WalkableMask
            };

            if (!TrySampleNavMeshPos(filter, out pos))
                return false;

            if (agent != null)
                pos += Vector3.up * agent.baseOffset;

            return true;
        }

        private bool TrySampleNavMeshPos(NavMeshQueryFilter filter, out Vector3 pos)
        {
            pos = Vector3.zero;

            if (navVerts == null)
            {
                NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
                navVerts = tri.vertices;
            }

            if (navVerts == null || navVerts.Length == 0)
                return false;

            for (int i = 0; i < SPAWN_POS_TRIES; i++)
            {
                Vector3 candidate = navVerts[Random.Range(0, navVerts.Length)]
                     + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));

                NavMeshHit hit;

                if (NavMesh.SamplePosition(candidate, out hit, SPAWN_SAMPLE_RADIUS, filter))
                {
                    pos = hit.position;
                    return true;
                }
            }

            return false;
        }

        public void RequestEat(int jellyNetId, int eaterNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return;

            if (net.IsHost)
            {
                ResolveEat(NetHost.HOST_ID, jellyNetId, eaterNetId);
                return;
            }

            w.Begin(MsgType.EatJellyRequest);
            w.WriteInt(jellyNetId);
            w.WriteInt(eaterNetId);
            w.End();
            net.SendToHost(w);
        }

        protected override void RegisterRoutes()
        {
            Net.RouteHost(MsgType.EatJellyRequest, (from, r) =>
            {
                int jellyNetId = r.ReadInt();
                int eaterNetId = r.ReadInt();
                ResolveEat(from, jellyNetId, eaterNetId);
            });

            Net.RouteHost(MsgType.AbsorbPlayerRequest, (from, r) =>
            {
                int victimNetId = r.ReadInt();
                int absorberNetId = r.ReadInt();
                ResolveAbsorbPlayer(from, victimNetId, absorberNetId);
            });

            Net.RouteClient(MsgType.EatJellyConfirm, r =>
            {
                int eaterNetId = r.ReadInt();
                int colorType = r.ReadInt();
                OnEatConfirmed(eaterNetId, colorType);
            });

            Net.RouteClient(MsgType.PlayerAbsorbed, r =>
            {
                int victimNetId = r.ReadInt();
                int absorberNetId = r.ReadInt();
                OnPlayerAbsorbed(victimNetId, absorberNetId);
            });
        }

        protected override void UnregisterRoutes()
        {
            Net.UnrouteHost(MsgType.EatJellyRequest);
            Net.UnrouteHost(MsgType.AbsorbPlayerRequest);
            Net.UnrouteClient(MsgType.EatJellyConfirm);
            Net.UnrouteClient(MsgType.PlayerAbsorbed);
        }

        private void ResolveEat(int requesterId, int jellyNetId, int eaterNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null)
                return;

            if (!LanGameFlow.IsPlaying(GameModeType.Absorb))
                return;

            NetIdentity jelly = NetWorld.Instance.Find(jellyNetId);
            if (jelly == null)
                return;
            if (!NetEntity.IsJelly(jelly))
            {
                return;
            }

            NetIdentity eater = NetWorld.Instance.Find(eaterNetId);
            if (eater == null)
                return;

            if (eater.OwnerId != requesterId)
                return;

            JellyObject jo = jelly.GetComponent<JellyObject>();
            int colorType = jo != null ? (int)jo.JellyType : (int)JellyColorType.None;

            NetWorld.Instance.HostDespawn(jellyNetId);

            w.Begin(MsgType.EatJellyConfirm);
            w.WriteInt(eaterNetId);
            w.WriteInt(colorType);
            w.End();
            net.Broadcast(w);

            OnEatConfirmed(eaterNetId, colorType);
        }

        private void CheckMyPlayerAbsorb()
        {
            if (myPlayer == null || NetWorld.Instance == null)
                return;
            if (NetEntity.IsOutOfPlay(myPlayer))
                return;

            float myScale = NetEntity.ScaleOf(myPlayer);
            Vector3 myPos = myPlayer.transform.position;

            int target = -1;

            NetEntity.CollectCharacters(characters);

            for (int i = 0; i < characters.Count; i++)
            {
                NetIdentity other = characters[i];
                if (other == null || other == myPlayer)
                    continue;

                if (!other.IsBot && other.OwnerId == myPlayer.OwnerId)
                    continue;

                if (NetEntity.IsOutOfPlay(other))
                    continue;

                float otherScale = NetEntity.ScaleOf(other);
                if (myScale < otherScale * absorbSizeRatio)
                    continue;

                Vector3 d = other.transform.position - myPos;
                d.y = 0f;
                float touch = (myScale + otherScale) * playerRadius;
                if (d.sqrMagnitude > touch * touch)
                    continue;

                target = other.NetId;
                break;
            }

            if (target >= 0)
                RequestAbsorbPlayer(target, myPlayer.NetId);
        }

        private void RequestAbsorbPlayer(int victimNetId, int absorberNetId)
        {
            NetManager net = NetManager.Instance;

            if (net.IsHost)
            {
                ResolveAbsorbPlayer(NetHost.HOST_ID, victimNetId, absorberNetId);
                return;
            }

            w.Begin(MsgType.AbsorbPlayerRequest);
            w.WriteInt(victimNetId);
            w.WriteInt(absorberNetId);
            w.End();
            net.SendToHost(w);
        }

        public void HostBotAbsorb(int victimNetId, int botNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;

            NetIdentity bot = NetWorld.Instance != null
                ? NetWorld.Instance.Find(botNetId) : null;

            if (bot != null && bot.OwnerId != NetHost.HOST_ID)
                return;

            ResolveAbsorbPlayer(NetHost.HOST_ID, victimNetId, botNetId);
        }

        private void ResolveAbsorbPlayer(int requesterId, int victimNetId, int absorberNetId)
        {
            HostJudgement judgement = HostJudgement.Judge(Mode, requesterId, absorberNetId, victimNetId);

            if (!judgement.Valid)
                return;

            NetIdentity absorber = judgement.Actor;
            NetIdentity victim = judgement.Target;

            if (NetEntity.IsJelly(victim) || NetEntity.IsJelly(absorber))
                return;

            float vScale = NetEntity.ScaleOf(victim);
            float aScale = NetEntity.ScaleOf(absorber);

            if (aScale < vScale * absorbSizeRatio)
                return;

            if (!judgement.WithinReach((aScale + vScale) * playerRadius * 1.5f))
                return;

            LanPlayerState victimState = victim.PlayerState;

            if (victimState != null)
            {
                victimState.HostSetFlag(PlayerFlags.Absorbed, true);
                victimState.HostSetFlag(PlayerFlags.Eliminated, true);
            }

            NetWorld.Instance.BroadcastGrow(absorberNetId, GrowKind.Absorbing, vScale);

            w.Begin(MsgType.PlayerAbsorbed);
            w.WriteInt(victimNetId);
            w.WriteInt(absorberNetId);
            w.End();
            NetManager.Instance.Broadcast(w);

            OnPlayerAbsorbed(victimNetId, absorberNetId);
        }

        private void OnPlayerAbsorbed(int victimNetId, int absorberNetId)
        {
            if (NetWorld.Instance == null)
                return;

            NetIdentity v = NetWorld.Instance.Find(victimNetId);
            NetIdentity a = NetWorld.Instance.Find(absorberNetId);

            if (v == null)
                return;

            Transform absorberTf = a != null ? a.transform : null;

            LanPlayerVisual victimVisual = v.Visual;
            if (victimVisual != null)
                victimVisual.PlayAbsorbed(absorberTf);
            else
                v.gameObject.SetActive(false);

            if (v.IsBot || !v.IsMine)
                return;

            LanSpectator.ReportKiller(absorberNetId);

            if (LanGameFlow.Instance != null)
                LanGameFlow.Instance.ShowLocalGameOver("흡수당했습니다!\n관전 중...");
        }

        private void OnEatConfirmed(int eaterNetId, int colorType)
        {
            NetIdentity eater = NetWorld.Instance != null ? NetWorld.Instance.Find(eaterNetId) : null;
            if (eater == null)
                return;

            PlayerAbsorber absorber = eater.GetComponentInChildren<PlayerAbsorber>(true);
            if (absorber != null)
                absorber.AbsorbColor((JellyColorType)colorType);
            else
            {
                LanPlayerVisual vis = eater.Visual;
                if (vis != null)
                    vis.ApplyJellyColor((JellyColorType)colorType);
            }
        }
    }
}
