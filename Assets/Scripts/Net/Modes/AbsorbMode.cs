using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class AbsorbMode : NetGameMode<AbsorbMode>
    {
        protected override GameModeType Mode
        {
            get { return GameModeType.Absorb; }
        }

        [Header("젤리 스폰 (호스트만 수행)")]
        public bool spawnJelly = true;
        public float spawnInterval = 1.5f;
        public int maxJellyCount = 30;
        public float spawnRangeX = 18f;
        public float spawnRangeZ = 18f;
        public float spawnHeight = 0.5f;

        [Header("먹기 판정")]
        [Tooltip("내 캡슐 반지름(스케일 1 기준)")]
        public float playerRadius = 0.5f;
        [Tooltip("젤리 반지름")]
        public float jellyRadius = 0.3f;

        [Tooltip("젤리가 빨려 들어가는 동안 플레이어가 도망칠 수 있는 거리. "
                 + "이동속도 × 흡수 연출 시간(약 0.6초)보다 넉넉해야 한다.")]
        public float eatChaseTolerance = 5f;
        [Tooltip("젤리 하나당 커지는 배율")]
        public float growPerJelly = 0.12f;
        [Tooltip("젤리 하나당 점수")]
        public int scorePerJelly = 1;

        [Tooltip("거리로 직접 먹기 검사(테스트 씬용). 실제 게임 씬에서는 꺼둔다 — " +
                 "PlayerAbsorber·JellyColliderAbsorb 연출 경로가 요청을 보낸다.")]
        public bool useDistanceEating = false;

        [Tooltip("흡수 요청이 어디서 거부되는지 콘솔에 찍는다. 문제 해결 후 끌 것.")]
        public bool verboseLog = true;

        [Header("플레이어 흡수")]
        [Tooltip("상대를 흡수하려면 내가 이 배수보다 커야 한다. 1이면 조금만 커도 됨.")]
        public float absorbSizeRatio = 1.15f;
        [Tooltip("흡수 시 상대 크기의 몇 %를 흡수하는가")]
        public float absorbGrowthRatio = 0.4f;
        [Tooltip("흡수 시 얻는 점수 = 상대 크기 × 이 값")]
        public int absorbScorePerScale = 10;
        [Tooltip("흡수당하면 부활시킬지. 원본 게임은 부활 없이 관전 전환이므로 기본 꺼짐.")]
        public bool respawnAfterAbsorb = false;
        [Tooltip("부활을 켰을 때의 대기 시간(초)")]
        public float respawnDelay = 3f;

        private readonly NetWriter w = new NetWriter();

        private readonly HashSet<int> jellies = new HashSet<int>();

        private readonly HashSet<int> runtimeJellies = new HashSet<int>();

        private readonly HashSet<int> requested = new HashSet<int>();

        private float spawnTimer;
        private NetIdentity myPlayer;

        struct PendingRespawn { public float At; public int NetId; }
        private readonly List<PendingRespawn> respawns = new List<PendingRespawn>();

        public int JellyCount { get { return jellies.Count; } }

        protected override void ResetAll()
        {
            jellies.Clear();
            runtimeJellies.Clear();
            requested.Clear();
            myPlayer = null;
            spawnTimer = 0f;
        }

        protected override void HandleSpawned(NetIdentity id)
        {
            if (NetEntity.IsJelly(id))
            {
                jellies.Add(id.NetId);
                if (id.NetId < NetConfig.SCENE_ID_BASE)
                    runtimeJellies.Add(id.NetId);
            }
            else if (id.IsMine && !id.IsBot)
                myPlayer = id;
        }

        protected override void HandleDespawned(int netId)
        {
            jellies.Remove(netId);
            runtimeJellies.Remove(netId);
            requested.Remove(netId);
            if (myPlayer != null && myPlayer.NetId == netId)
                myPlayer = null;
        }


        private void Log(string msg)
        {
            if (verboseLog)
                Debug.Log("[흡수] " + msg);
        }

        private void Update()
        {
            if (IsOffline)
                return;

            if (IsHost && IsCurrentMode)
            {
                HostSpawnTick();
                HostRespawnTick();
            }

            if (!IsPlaying)
                return;

            if (useDistanceEating)
                CheckMyEating();

            CheckMyPlayerAbsorb();
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
                Debug.LogWarning("[AbsorbMode] 젤리 프리팹이 등록되지 않았습니다 (Prefabs 1번 이후).");
                spawnJelly = false;
                return;
            }

            int prefabId = PickJellyPrefab(prefabs);
            if (prefabId < 0)
                return;

            Vector3 pos = PickJellySpawnPos(prefabs[prefabId]);

            NetIdentity spawned = NetWorld.Instance.HostSpawn(prefabId, NetHost.HOST_ID, pos);

            if (spawned == null)
                return;

            UnityEngine.AI.NavMeshAgent ag =
                spawned.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();

            if (ag == null)
                return;

            ag.enabled = false;
            ag.transform.position = pos;
            ag.enabled = true;

            if (!ag.isOnNavMesh)
                Log("젤리 net" + spawned.NetId + " 가 NavMesh에 못 올라감 — 위치 " + pos);
        }

        private List<int> jellyPrefabIds;

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

                if (jellyPrefabIds.Count == 0)
                    Debug.LogWarning("[AbsorbMode] 젤리로 쓸 프리팹이 없습니다. "
                                     + "NetWorld.prefabs의 1번 이후를 확인해주세요.");
            }

            if (jellyPrefabIds.Count == 0)
                return -1;
            return jellyPrefabIds[Random.Range(0, jellyPrefabIds.Count)];
        }

        private Vector3[] navVerts;

        private const int SPAWN_POS_TRIES = 8;
        private const float SPAWN_SAMPLE_RADIUS = 8f;

        //NavMeshAgent는 baseOffset만큼 떠 있는 걸 전제로 자리를 잡는다
        //표면 좌표를 그대로 주면 발밑이 NavMesh 아래로 내려가 "not close enough" 로 붙지 못한다
        private Vector3 PickJellySpawnPos(GameObject prefab)
        {
            Vector3 pos = SampleNavMeshPos();

            UnityEngine.AI.NavMeshAgent agent = prefab != null
                ? prefab.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true)
                : null;

            if (agent != null)
                pos += Vector3.up * agent.baseOffset;

            return pos;
        }

        private Vector3 SampleNavMeshPos()
        {
            if (navVerts == null)
            {
                UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
                navVerts = tri.vertices;

                if (navVerts == null || navVerts.Length == 0)
                    Debug.LogWarning("[AbsorbMode] NavMesh가 없습니다 — 젤리를 원점 근처에 뿌립니다. "
                                     + "맵에 NavMesh를 구워야 움직이는 젤리가 제대로 배치됩니다.");
            }

            if (navVerts == null || navVerts.Length == 0)
                return new Vector3(Random.Range(-spawnRangeX, spawnRangeX), spawnHeight,
                                   Random.Range(-spawnRangeZ, spawnRangeZ));

            //navVerts는 최초 1회 캐시라 무너진 발판 위 좌표가 섞여 있다
            //한 번 실패했다고 그 자리에 두지 말고 다른 정점을 몇 번 더 뽑는다
            Vector3 last = Vector3.zero;

            for (int i = 0; i < SPAWN_POS_TRIES; i++)
            {
                last = navVerts[Random.Range(0, navVerts.Length)]
                     + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));

                UnityEngine.AI.NavMeshHit hit;

                if (UnityEngine.AI.NavMesh.SamplePosition(last, out hit, SPAWN_SAMPLE_RADIUS,
                        UnityEngine.AI.NavMesh.AllAreas))
                    return hit.position;
            }

            return last;
        }

        private void CheckMyEating()
        {
            if (myPlayer == null)
            {
                myPlayer = NetEntity.FindMyPlayer();

                if (myPlayer == null)
                    return;
            }
            if (NetWorld.Instance == null)
                return;

            float myR = playerRadius * NetEntity.ScaleOf(myPlayer);
            Vector3 myPos = myPlayer.transform.position;

            int target = -1;
            float reach = (myR + jellyRadius) * (myR + jellyRadius);

            foreach (int jellyId in jellies)
            {
                if (requested.Contains(jellyId))
                    continue;

                NetIdentity jelly = NetWorld.Instance.Find(jellyId);
                if (jelly == null)
                    continue;

                Vector3 d = jelly.transform.position - myPos;
                d.y = 0f;
                if (d.sqrMagnitude > reach)
                    continue;

                target = jellyId;
                break;
            }

            if (target >= 0)
                RequestEat(target, myPlayer.NetId);
        }


        public void RequestEat(int jellyNetId, int eaterNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null)
            {
                Log("요청 불가 — NetManager 없음");
                return;
            }

            Log("요청: 젤리 net" + jellyNetId + " ← 먹는이 net" + eaterNetId
                + " (내 모드 " + net.CurrentMode + ")");

            requested.Add(jellyNetId);

            if (net.IsHost)
            {
                ResolveEat(NetHost.HOST_ID, jellyNetId, eaterNetId);
                return;
            }

            w.Begin(MsgType.EatJellyRequest);
            w.WriteInt(jellyNetId);
            w.WriteInt(eaterNetId);
            w.End();
            net.Client.Send(w);
        }

        protected override void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type == MsgType.EatJellyRequest)
            {
                int jellyNetId = r.ReadInt();
                int eaterNetId = r.ReadInt();
                ResolveEat(from.Id, jellyNetId, eaterNetId);
            }
            else if (type == MsgType.AbsorbPlayerRequest)
            {
                int victimNetId = r.ReadInt();
                int absorberNetId = r.ReadInt();
                ResolveAbsorbPlayer(from.Id, victimNetId, absorberNetId);
            }
        }

        private void ResolveEat(int requesterId, int jellyNetId, int eaterNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null)
                return;

            if (!LanGameFlow.IsPlaying(GameModeType.Absorb))
            { Log("거부: 진행 중이 아님 (단계 " + (LanGameFlow.Instance != null ? LanGameFlow.Instance.Phase.ToString() : "?") + ")"); return; }

            NetIdentity jelly = NetWorld.Instance.Find(jellyNetId);
            if (jelly == null)
            {
                Log("탈락: 젤리 net" + jellyNetId + " 없음(이미 먹혔거나 네트워크 오브젝트가 아님)");
                return;
            }
            if (!NetEntity.IsJelly(jelly))
            {
                Log("거부: net" + jellyNetId + " 는 젤리가 아님 (prefabId " + jelly.PrefabId + ")");
                return;
            }

            NetIdentity eater = NetWorld.Instance.Find(eaterNetId);
            if (eater == null)
            {
                Log("탈락: 먹는이 net" + eaterNetId + " 없음");
                return;
            }

            if (eater.OwnerId != requesterId)
            { Log("거부: 소유권 불일치 (요청자 P" + requesterId + " ≠ 소유자 P" + eater.OwnerId + ")"); return; }

            NetScale es = eater.GetComponent<NetScale>();
            float eaterR = playerRadius * NetEntity.ScaleOf(eater);
            Vector3 gap = jelly.transform.position - eater.transform.position;
            gap.y = 0f;
            float allow = (eaterR + jellyRadius) * 2.5f + eatChaseTolerance;
            if (gap.sqrMagnitude > allow * allow)
            { Log("거부: 너무 멂 (거리 " + gap.magnitude.ToString("F2") + " > 허용 " + allow.ToString("F2") + ")"); return; }

            JellyObject jo = jelly.GetComponent<JellyObject>();
            int colorType = jo != null ? (int)jo.jellyType : (int)JellyColorType.None;

            NetWorld.Instance.HostDespawn(jellyNetId);

            if (es != null)
                es.HostGrow(growPerJelly);

            w.Begin(MsgType.EatJellyConfirm);
            w.WriteInt(eaterNetId);
            w.WriteInt(colorType);
            w.End();
            net.Host.Broadcast(w);

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
            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity other = kv.Value;
                if (other == null || other == myPlayer)
                    continue;
                if (NetEntity.IsJelly(other))
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
            net.Client.Send(w);
        }

        public void HostAbsorb(int victimNetId, int absorberNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;
            ResolveAbsorbPlayer(NetHost.HOST_ID, victimNetId, absorberNetId);
        }

        private void ResolveAbsorbPlayer(int requesterId, int victimNetId, int absorberNetId)
        {
            HostVerdict verdict = HostVerdict.Judge(Mode, requesterId, absorberNetId, victimNetId);

            if (!verdict.Valid)
                return;

            NetIdentity absorber = verdict.Actor;
            NetIdentity victim = verdict.Target;

            if (NetEntity.IsJelly(victim) || NetEntity.IsJelly(absorber))
                return;

            float vScale = NetEntity.ScaleOf(victim);
            float aScale = NetEntity.ScaleOf(absorber);

            if (aScale < vScale * absorbSizeRatio)
                return;

            //몸이 닿는 거리인가. 지연을 감안해 여유를 둔다
            if (!verdict.WithinReach((aScale + vScale) * playerRadius * 1.5f))
                return;

            LanPlayerState vs = victim.GetComponent<LanPlayerState>();
            LanPlayerState asx = absorber.GetComponent<LanPlayerState>();
            NetScale vsc = victim.GetComponent<NetScale>();
            NetScale asc = absorber.GetComponent<NetScale>();

            if (vs != null)
            {
                vs.HostSetFlag(PlayerFlags.Absorbed, true);
                vs.HostSetFlag(PlayerFlags.Eliminated, true);
            }

            LanPlayerVisual avis = absorber.GetComponent<LanPlayerVisual>();
            float victimScaleValue = avis != null
                ? (victim.GetComponent<LanPlayerVisual>() != null
                    ? victim.GetComponent<LanPlayerVisual>().ScaleValue : vScale)
                : vScale;
            NetWorld.Instance.BroadcastGrow(absorberNetId, GrowKind.Absorbing, victimScaleValue);

            if (asc != null)
                asc.HostGrow(vScale * absorbGrowthRatio);

            if (vsc != null)
            {
                vsc.SetTarget(1f);
                NetWorld.Instance.BroadcastScale(victimNetId, 1f);
            }

            w.Begin(MsgType.PlayerAbsorbed);
            w.WriteInt(victimNetId);
            w.WriteInt(absorberNetId);
            w.End();
            NetManager.Instance.Host.Broadcast(w);

            OnPlayerAbsorbed(victimNetId, absorberNetId);

            if (respawnAfterAbsorb)
            {
                PendingRespawn pr;
                pr.At = Time.time + respawnDelay;
                pr.NetId = victimNetId;
                respawns.Add(pr);
            }
        }

        private void HostRespawnTick()
        {
            if (respawns.Count == 0)
                return;

            float now = Time.time;
            for (int i = respawns.Count - 1; i >= 0; i--)
            {
                if (respawns[i].At > now)
                    continue;

                int netId = respawns[i].NetId;
                respawns.RemoveAt(i);

                NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
                if (id == null)
                    continue;

                Vector3 pos = LanSpawnPoints.Instance != null
                    ? LanSpawnPoints.Instance.Random_()
                    : new Vector3(Random.Range(-spawnRangeX, spawnRangeX), spawnHeight,
                                  Random.Range(-spawnRangeZ, spawnRangeZ));

                LanPlayerState ps = id.GetComponent<LanPlayerState>();
                if (ps != null)
                    ps.HostSetFlag(PlayerFlags.Absorbed, false);

                w.Begin(MsgType.PlayerRespawn);
                w.WriteInt(netId);
                w.WriteFloat(pos.x); w.WriteFloat(pos.y); w.WriteFloat(pos.z);
                w.End();
                NetManager.Instance.Host.Broadcast(w);

                ApplyRespawn(netId, pos);
            }
        }

        private void OnPlayerAbsorbed(int victimNetId, int absorberNetId)
        {
            if (NetWorld.Instance == null)
                return;

            NetIdentity v = NetWorld.Instance.Find(victimNetId);
            NetIdentity a = NetWorld.Instance.Find(absorberNetId);

            NetManager.Instance.AddLog(
                "P" + (a != null ? a.OwnerId : 0) + " 가 P" + (v != null ? v.OwnerId : 0) + " 를 흡수!");

            if (v == null)
                return;

            Transform absorberTf = a != null ? a.transform : null;

            if (v.IsBot)
            {
                AIPlayerMovement bot = v.GetComponent<AIPlayerMovement>();
                if (bot != null)
                    bot.ApplyAbsorbedFromNet(absorberTf);
                else
                    v.gameObject.SetActive(false);
                return;
            }

            LanPlayerVisual vv = v.GetComponent<LanPlayerVisual>();
            if (vv != null)
                vv.PlayAbsorbed(absorberTf);
            else
                v.gameObject.SetActive(false);

            if (!v.IsMine)
                return;

            LanSpectator.ReportKiller(absorberNetId);

            if (LanGameFlow.Instance != null)
                LanGameFlow.Instance.ShowLocalGameOver("흡수당했습니다!\n관전 중...");
        }

        private void ApplyRespawn(int netId, Vector3 pos)
        {
            NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
            if (id == null)
                return;

            if (id.IsMine)
                id.transform.position = pos;

            NetScale ns = id.GetComponent<NetScale>();
            if (ns != null)
                ns.SetImmediate(1f);

            NetManager.Instance.AddLog("net" + id.NetId + " 부활");
        }



        protected override void HandleClientMessage(MsgType type, NetReader r)
        {
            switch (type)
            {
                case MsgType.EatJellyConfirm:
                    OnEatConfirmed(r.ReadInt(), r.ReadInt());
                    break;

                case MsgType.PlayerAbsorbed:
                    {
                        int victimNetId = r.ReadInt();
                        int absorberNetId = r.ReadInt();
                        OnPlayerAbsorbed(victimNetId, absorberNetId);
                        break;
                    }

                case MsgType.PlayerRespawn:
                    {
                        int netId = r.ReadInt();
                        float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat();
                        ApplyRespawn(netId, new Vector3(x, y, z));
                        break;
                    }
            }
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
                LanPlayerVisual vis = eater.GetComponent<LanPlayerVisual>();
                if (vis != null)
                    vis.ApplyJellyColor((JellyColorType)colorType);
            }

            NetManager.Instance.AddLog("P" + eater.OwnerId + " 흡수! (" + (JellyColorType)colorType + ")");
        }
    }
}
