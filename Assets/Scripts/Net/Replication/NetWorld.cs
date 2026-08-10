using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetWorld : MonoBehaviour
    {
        public static NetWorld Instance { get; private set; }

        [Header("프리팹 등록표 (배열 인덱스 = prefabId)")]
        [Tooltip("0번은 플레이어 캡슐. 순서가 곧 ID이므로 중간에 끼워넣지 말 것.")]
        public GameObject[] prefabs;

        [Header("스폰 위치")]
        public float spawnRadius = 4f;

        private readonly Dictionary<int, NetIdentity> objects = new Dictionary<int, NetIdentity>();

        private readonly NetWriter w = new NetWriter();
        private int nextNetId = 1;

        private readonly List<int> removedSceneIds = new List<int>();

        public IReadOnlyDictionary<int, NetIdentity> Objects { get { return objects; } }

        public event System.Action<NetIdentity> OnSpawned;
        public event System.Action<int> OnDespawned;

        public NetIdentity Find(int netId)
        {
            NetIdentity id;
            return objects.TryGetValue(netId, out id) ? id : null;
        }

        //스폰 메시지에 싣는 값은 NetScale 배율이다
        //판정에 쓰는 NetEntity.ScaleOf(게임 쪽 실제 크기)와 단위가 다르니 섞지 말 것
        private static float NetScaleOf(NetIdentity id)
        {
            NetScale scale = id.GetComponent<NetScale>();
            return scale != null ? scale.Current : 1f;
        }

        private NetSpawnPool pool;

        public NetSpawnPool Pool
        {
            get
            {
                pool ??= new NetSpawnPool(prefabs, transform);
                return pool;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }


        private void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
            {
                Debug.LogError("[NetWorld] NetManager가 없습니다.");
                return;
            }
            net.OnHostStarted += HandleHostStarted;
            net.OnPeerJoined += HandlePeerJoined;
            net.OnPeerLeft += HandlePeerLeft;
            net.OnHostMessage += HandleHostMessage;
            net.OnClientMessage += HandleClientMessage;
            net.OnDisconnected += ClearAll;

            ValidatePrefabs();
            RegisterSceneObjects();

            Start_CatchUpNetwork();
        }

        private void RegisterSceneObjects()
        {
            int n = 0;
            foreach (NetIdentity id in FindObjectsByType<NetIdentity>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (id == null || id.SceneNetId == 0)
                    continue;

                id.NetId = id.SceneNetId;
                id.OwnerId = 0;
                if (id.PrefabId == 0)
                    id.PrefabId = NetConfig.JELLY_PREFAB_START;

                objects[id.NetId] = id;
                if (OnSpawned != null)
                    OnSpawned(id);
                n++;
            }

            if (n > 0)
                Debug.Log("[NetWorld] 씬 배치 오브젝트 " + n + "개 등록");
            else
                Debug.LogWarning("[NetWorld] 씬 배치 오브젝트가 하나도 등록되지 않았습니다. "
                                  + "Tools ▸ LAN 이식 ▸ ⑦ 씬 오브젝트 ID 부여 를 실행하세요.");
        }

        private void ValidatePrefabs()
        {
            if (prefabs == null || prefabs.Length == 0)
            {
                Debug.LogError("[NetWorld] Prefabs 배열이 비어 있습니다. 플레이어 프리팹을 등록하세요.");
                return;
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null)
                {
                    Debug.LogError("[NetWorld] Prefabs[" + i + "] 가 비어 있습니다.");
                    continue;
                }
                if (prefabs[i].GetComponent<NetIdentity>() == null)
                    Debug.LogError("[NetWorld] '" + prefabs[i].name + "' 에 NetIdentity가 없습니다.");

                if (i < NetConfig.JELLY_PREFAB_START && prefabs[i].GetComponent<NetTransform>() == null)
                    Debug.LogError("[NetWorld] '" + prefabs[i].name + "' 에 NetTransform이 없습니다 — 위치 동기화가 전혀 안 됩니다!");

                if (prefabs[i].GetComponent<NetScale>() == null)
                    Debug.LogWarning("[NetWorld] '" + prefabs[i].name + "' 에 NetScale이 없습니다 — 흡수해도 크기가 안 변합니다.");
            }
        }

        private void OnDestroy()
        {
            pool?.Clear();

            NetManager net = NetManager.Instance;
            if (net == null)
                return;
            net.OnHostStarted -= HandleHostStarted;
            net.OnPeerJoined -= HandlePeerJoined;
            net.OnPeerLeft -= HandlePeerLeft;
            net.OnHostMessage -= HandleHostMessage;
            net.OnClientMessage -= HandleClientMessage;
            net.OnDisconnected -= ClearAll;
        }

        private void Start_CatchUpNetwork()
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None)
                return;

            if (net.IsHost)
            {
                SpawnForOwner(NetHost.HOST_ID);
                return;
            }

            StartCoroutine(SceneReadyLoop());
        }

        private System.Collections.IEnumerator SceneReadyLoop()
        {
            const float INTERVAL = 0.4f;
            const float GIVE_UP_AFTER = 20f;

            float waited = 0f;

            while (waited < GIVE_UP_AFTER)
            {
                NetManager net = NetManager.Instance;
                if (net == null || net.CurrentMode != NetManager.Mode.Client)
                    yield break;

                if (HasMyObject())
                    yield break;

                w.Begin(MsgType.SceneReady);
                w.End();
                net.Client.Send(w);

                yield return new WaitForSeconds(INTERVAL);
                waited += INTERVAL;
            }

            Debug.LogWarning("[NetWorld] 호스트가 내 캐릭터를 만들어주지 않습니다. "
                             + "호스트가 게임 씬에 정상적으로 들어왔는지 확인해주세요.");
        }

        private bool HasMyObject()
        {
            foreach (var kv in objects)
                if (kv.Value != null && kv.Value.IsMine && !kv.Value.IsBot)
                    return true;
            return false;
        }

        private void HandleHostStarted()
        {
            SpawnForOwner(NetHost.HOST_ID);
        }

        private void HandleSceneReady(NetHost.Peer peer)
        {
            SendWorldSnapshot(peer);
            SpawnForOwner(peer.Id);
        }

        private void HandlePeerJoined(NetHost.Peer peer)
        {
            SendWorldSnapshot(peer);
            SpawnForOwner(peer.Id);
        }

        private void SendWorldSnapshot(NetHost.Peer peer)
        {
            foreach (var kv in objects)
            {
                NetIdentity id = kv.Value;

                if (id.NetId >= NetConfig.SCENE_ID_BASE)
                    continue;

                WriteSpawn(id.NetId, id.PrefabId, id.OwnerId, id.transform.position, NetScaleOf(id));
                NetManager.Instance.Host.SendTo(peer, w);

                LanPlayerState ps = id.GetComponent<LanPlayerState>();
                if (ps != null)
                {
                    WritePlayerState(id.NetId, ps.Score, (byte)ps.Flags, ps.DisplayColor);
                    NetManager.Instance.Host.SendTo(peer, w);

                    if (!string.IsNullOrEmpty(ps.PlayerName))
                    {
                        w.Begin(MsgType.PlayerNameSet);
                        w.WriteInt(id.NetId);
                        w.WriteString(ps.PlayerName);
                        w.End();
                        NetManager.Instance.Host.SendTo(peer, w);
                    }
                }
            }

            for (int i = 0; i < removedSceneIds.Count; i++)
            {
                w.Begin(MsgType.DespawnEntity);
                w.WriteInt(removedSceneIds[i]);
                w.End();
                NetManager.Instance.Host.SendTo(peer, w);
            }
        }

        private void HandlePeerLeft(NetHost.Peer peer)
        {
            List<int> toRemove = new List<int>();
            foreach (var kv in objects)
                if (kv.Value.OwnerId == peer.Id)
                    toRemove.Add(kv.Key);

            for (int i = 0; i < toRemove.Count; i++)
                HostDespawn(toRemove[i]);
        }

        public NetIdentity SpawnForOwner(int ownerId, int prefabId = 0)
        {
            if (!NetManager.Instance.IsHost)
                return null;

            foreach (var kv in objects)
            {
                NetIdentity ex = kv.Value;
                if (ex == null || ex.OwnerId != ownerId)
                    continue;
                if (ex.IsBot)
                    continue;
                if (ex.PrefabId != prefabId)
                    continue;
                return ex;
            }

            return HostSpawn(prefabId, ownerId, PickPlayerSpawnPos());
        }

        private Vector3 PickPlayerSpawnPos()
        {
            if (LanSpawnPoints.Instance != null)
                return LanSpawnPoints.Instance.Take();
            return PickSpawnPos(nextNetId);
        }

        public NetIdentity HostSpawn(int prefabId, int ownerId, Vector3 pos)
        {
            if (!NetManager.Instance.IsHost)
                return null;

            int netId = nextNetId++;
            NetIdentity id = SpawnLocal(netId, prefabId, ownerId, pos, 1f);

            WriteSpawn(netId, prefabId, ownerId, pos, 1f);
            NetManager.Instance.Host.Broadcast(w);
            return id;
        }

        public void BroadcastGrow(int netId, GrowKind kind, float amount)
        {
            if (!NetManager.Instance.IsHost)
                return;

            w.Begin(MsgType.GrowEvent);
            w.WriteInt(netId);
            w.WriteByte((byte)kind);
            w.WriteFloat(amount);
            w.End();
            NetManager.Instance.Host.Broadcast(w);

            NetIdentity id = Find(netId);
            if (id != null)
            {
                LanPlayerVisual v = id.GetComponent<LanPlayerVisual>();
                if (v != null)
                    v.ApplyGrow(kind, amount);
            }
        }

        public void RelayAnimState(NetHost.Peer from, int netId, byte kind, byte value)
        {
            if (!NetManager.Instance.IsHost)
                return;

            w.Begin(MsgType.AnimState);
            w.WriteInt(netId);
            w.WriteByte(kind);
            w.WriteByte(value);
            w.End();
            NetManager.Instance.Host.BroadcastExcept(from, w);

            NetIdentity id = Find(netId);
            if (id != null && !id.IsMine)
            {
                LanPlayerVisual v = id.GetComponent<LanPlayerVisual>();
                if (v != null)
                    v.ApplyAnim(kind, value);
            }
        }

        public void BroadcastScale(int netId, float scale)
        {
            if (!NetManager.Instance.IsHost)
                return;

            w.Begin(MsgType.StateUpdate);
            w.WriteInt(netId);
            w.WriteFloat(scale);
            w.End();
            NetManager.Instance.Host.Broadcast(w);
        }

        public void BroadcastTileCollapse(int x, int z)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;

            w.Begin(MsgType.TileCollapse);
            w.WriteInt(x);
            w.WriteInt(z);
            w.End();
            net.Host.Broadcast(w);
        }

        public void BroadcastTileWear(int x, int z, int count, int maxSteps)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;

            w.Begin(MsgType.TileWear);
            w.WriteInt(x);
            w.WriteInt(z);
            w.WriteByte((byte)Mathf.Clamp(count, 0, 255));
            w.WriteByte((byte)Mathf.Clamp(maxSteps, 1, 255));
            w.End();
            net.Host.Broadcast(w);
        }

        public void BroadcastPlayerState(int netId, int score, byte flags, Color color)
        {
            if (!NetManager.Instance.IsHost)
                return;

            WritePlayerState(netId, score, flags, color);
            NetManager.Instance.Host.Broadcast(w);
        }

        public void BroadcastPlayerName(int netId, string name)
        {
            if (!NetManager.Instance.IsHost)
                return;

            w.Begin(MsgType.PlayerNameSet);
            w.WriteInt(netId);
            w.WriteString(name);
            w.End();
            NetManager.Instance.Host.Broadcast(w);
        }

        private void WritePlayerState(int netId, int score, byte flags, Color color)
        {
            w.Begin(MsgType.PlayerStateUpdate);
            w.WriteInt(netId);
            w.WriteInt(score);
            w.WriteByte(flags);
            w.WriteFloat(color.r);
            w.WriteFloat(color.g);
            w.WriteFloat(color.b);
            w.End();
        }

        public void HostDespawn(int netId)
        {
            if (!NetManager.Instance.IsHost)
                return;

            DespawnLocal(netId);

            w.Begin(MsgType.DespawnEntity);
            w.WriteInt(netId);
            w.End();
            NetManager.Instance.Host.Broadcast(w);
        }

        private void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type == MsgType.SetMyName)
            {
                string name = r.ReadString();
                if (string.IsNullOrEmpty(name))
                    return;
                if (name.Length > 16)
                    name = name.Substring(0, 16);

                foreach (var kv in objects)
                {
                    NetIdentity owned = kv.Value;
                    if (owned == null || owned.OwnerId != from.Id)
                        continue;

                    LanPlayerState ps = owned.GetComponent<LanPlayerState>();
                    if (ps == null)
                        continue;

                    ps.HostSetName(name);
                    break;
                }
                return;
            }

            if (type == MsgType.SceneReady)
            {
                HandleSceneReady(from);
                return;
            }

            if (type == MsgType.AnimState)
            {
                int aNetId = r.ReadInt();
                byte kind = r.ReadByte();
                byte value = r.ReadByte();

                NetIdentity a = Find(aNetId);
                if (a != null && a.OwnerId == from.Id)
                    RelayAnimState(from, aNetId, kind, value);
                return;
            }

            if (type != MsgType.TransformUpdate)
                return;

            int netId = r.ReadInt();
            float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat(), yaw = r.ReadFloat();

            NetIdentity id;
            if (!objects.TryGetValue(netId, out id))
                return;

            if (id.OwnerId != from.Id)
                return;

            ApplyTransform(id, new Vector3(x, y, z), yaw);

            WriteTransform(netId, new Vector3(x, y, z), yaw);
            NetManager.Instance.Host.BroadcastExcept(from, w);
        }

        private void HandleClientMessage(MsgType type, NetReader r)
        {
            switch (type)
            {
                case MsgType.SpawnEntity:
                    {
                        int netId = r.ReadInt();
                        int prefabId = r.ReadInt();
                        int ownerId = r.ReadInt();
                        float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat();
                        float scale = r.ReadFloat();
                        SpawnLocal(netId, prefabId, ownerId, new Vector3(x, y, z), scale);
                        break;
                    }

                case MsgType.StateUpdate:
                    {
                        int netId = r.ReadInt();
                        float scale = r.ReadFloat();
                        NetIdentity id = Find(netId);
                        if (id != null)
                        {
                            NetScale ns = id.GetComponent<NetScale>();
                            if (ns != null)
                                ns.SetTarget(scale);
                        }
                        break;
                    }

                case MsgType.PlayerStateUpdate:
                    {
                        int netId = r.ReadInt();
                        int score = r.ReadInt();
                        byte flags = r.ReadByte();
                        float cr = r.ReadFloat(), cg = r.ReadFloat(), cb = r.ReadFloat();

                        NetIdentity id = Find(netId);
                        if (id != null)
                        {
                            LanPlayerState ps = id.GetComponent<LanPlayerState>();
                            if (ps != null)
                                ps.ApplyState(score, flags, new Color(cr, cg, cb, 1f));
                        }
                        break;
                    }

                case MsgType.GrowEvent:
                    {
                        int netId = r.ReadInt();
                        GrowKind kind = (GrowKind)r.ReadByte();
                        float amount = r.ReadFloat();

                        NetIdentity id = Find(netId);
                        if (id != null)
                        {
                            LanPlayerVisual v = id.GetComponent<LanPlayerVisual>();
                            if (v != null)
                                v.ApplyGrow(kind, amount);
                        }
                        break;
                    }

                case MsgType.AnimState:
                    {
                        int netId = r.ReadInt();
                        byte kind = r.ReadByte();
                        byte value = r.ReadByte();

                        NetIdentity id = Find(netId);
                        if (id != null && !id.IsMine)
                        {
                            LanPlayerVisual v = id.GetComponent<LanPlayerVisual>();
                            if (v != null)
                                v.ApplyAnim(kind, value);
                        }
                        break;
                    }

                case MsgType.TileCollapse:
                    {
                        int tx = r.ReadInt();
                        int tz = r.ReadInt();
                        if (TileCollapseManager.Instance != null)
                            TileCollapseManager.Instance.CollapseStepTile(tx, tz, false);
                        break;
                    }

                case MsgType.BotState:
                    {
                        int netId = r.ReadInt();
                        float s = r.ReadFloat();
                        float cr = r.ReadFloat();
                        float cg = r.ReadFloat();
                        float cb = r.ReadFloat();
                        NetIdentity id = Find(netId);
                        if (id != null)
                        {
                            LanBotSync bs = id.GetComponent<LanBotSync>();
                            if (bs != null)
                                bs.ApplyState(s, new Color(cr, cg, cb, 1f));
                        }
                        break;
                    }

                case MsgType.BotEliminated:
                    {
                        int netId = r.ReadInt();
                        NetIdentity id = Find(netId);
                        if (id != null)
                        {
                            AIPlayerMovement bot = id.GetComponent<AIPlayerMovement>();
                            if (bot != null)
                                bot.ApplyEliminatedFromNet();
                        }
                        break;
                    }

                case MsgType.TileWear:
                    {
                        int tx = r.ReadInt();
                        int tz = r.ReadInt();
                        int count = r.ReadByte();
                        int maxSteps = r.ReadByte();
                        if (TileCollapseManager.Instance != null)
                            TileCollapseManager.Instance.DarkenStepTile(tx, tz, count, maxSteps);
                        break;
                    }

                case MsgType.PlayerNameSet:
                    {
                        int netId = r.ReadInt();
                        string name = r.ReadString();

                        NetIdentity id = Find(netId);
                        if (id != null)
                        {
                            LanPlayerState ps = id.GetComponent<LanPlayerState>();
                            if (ps != null)
                                ps.ApplyName(name);
                        }
                        break;
                    }

                case MsgType.DespawnEntity:
                    DespawnLocal(r.ReadInt());
                    break;

                case MsgType.TransformUpdate:
                    {
                        int netId = r.ReadInt();
                        float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat(), yaw = r.ReadFloat();
                        NetIdentity id;
                        if (objects.TryGetValue(netId, out id))
                            ApplyTransform(id, new Vector3(x, y, z), yaw);
                        break;
                    }
            }
        }

        private NetIdentity SpawnLocal(int netId, int prefabId, int ownerId, Vector3 pos, float scale)
        {
            if (objects.ContainsKey(netId))
                return objects[netId];

            if (prefabs == null || prefabId < 0 || prefabId >= prefabs.Length || prefabs[prefabId] == null)
            {
                Debug.LogError("[NetWorld] prefabId " + prefabId + " 에 해당하는 프리팹이 없습니다.");
                return null;
            }

            GameObject go = Pool.Get(prefabId, pos);
            go.name = prefabs[prefabId].name + "_net" + netId + "_own" + ownerId;

            NetIdentity id = go.GetComponent<NetIdentity>();
            if (id == null)
                id = go.AddComponent<NetIdentity>();

            EnsurePlayerComponents(go);

            id.NetId = netId;
            id.OwnerId = ownerId;
            id.PrefabId = prefabId;

            NetScale ns = id.GetComponent<NetScale>();
            if (ns != null)
                ns.SetImmediate(scale);

            LanPlayerSetup setup = id.GetComponent<LanPlayerSetup>();
            if (setup != null)
                setup.Apply();

            objects[netId] = id;
            NetManager.Instance.AddLog("스폰: net" + netId + " (프리팹 " + prefabId + ", 소유 P" + ownerId + ")");

            if (OnSpawned != null)
                OnSpawned(id);
            return id;
        }

        private static void EnsurePlayerComponents(GameObject go)
        {
            if (go.GetComponentInChildren<PlayerMovement>(true) == null)
                return;

            if (go.GetComponent<LanPlayerSetup>() == null)
            {
                go.AddComponent<LanPlayerSetup>();
                Debug.LogWarning("[NetWorld] " + go.name + " 에 LanPlayerSetup이 없어 런타임에 추가했습니다. "
                                 + "프리팹에 직접 붙여두는 편이 좋습니다.");
            }
            if (go.GetComponent<LanPlayerVisual>() == null)
            {
                go.AddComponent<LanPlayerVisual>();
                Debug.LogWarning("[NetWorld] " + go.name + " 에 LanPlayerVisual이 없어 런타임에 추가했습니다. "
                                 + "프리팹에 직접 붙여두는 편이 좋습니다.");
            }
            if (go.GetComponent<LanPlayerState>() == null)
                go.AddComponent<LanPlayerState>();
            if (go.GetComponent<NetKnockback>() == null)
                go.AddComponent<NetKnockback>();
        }

        private void DespawnLocal(int netId)
        {
            NetIdentity id;
            if (!objects.TryGetValue(netId, out id))
                return;

            objects.Remove(netId);

            if (id != null)
            {
                //씬에 미리 배치된 오브젝트는 우리가 만든 게 아니라 풀에 넣을 수 없다
                if (netId >= NetConfig.SCENE_ID_BASE)
                    Destroy(id.gameObject);
                else
                    Pool.Release(id.gameObject);
            }

            if (netId >= NetConfig.SCENE_ID_BASE)
                removedSceneIds.Add(netId);

            if (OnDespawned != null)
                OnDespawned(netId);
        }

        private void ApplyTransform(NetIdentity id, Vector3 pos, float yaw)
        {
            NetTransform nt = id.GetComponent<NetTransform>();
            if (nt != null)
                nt.OnRemoteTransform(pos, yaw);
            else { id.transform.position = pos; id.transform.rotation = Quaternion.Euler(0, yaw, 0); }
        }

        public void ClearAll()
        {
            foreach (var kv in objects)
            {
                if (kv.Key >= NetConfig.SCENE_ID_BASE)
                    continue;
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            }
            objects.Clear();
            removedSceneIds.Clear();
            nextNetId = 1;

            RegisterSceneObjects();
        }

        private void WriteSpawn(int netId, int prefabId, int ownerId, Vector3 pos, float scale)
        {
            w.Begin(MsgType.SpawnEntity);
            w.WriteInt(netId);
            w.WriteInt(prefabId);
            w.WriteInt(ownerId);
            w.WriteFloat(pos.x); w.WriteFloat(pos.y); w.WriteFloat(pos.z);
            w.WriteFloat(scale);
            w.End();
        }

        private void WriteTransform(int netId, Vector3 pos, float yaw)
        {
            w.Begin(MsgType.TransformUpdate);
            w.WriteInt(netId);
            w.WriteFloat(pos.x); w.WriteFloat(pos.y); w.WriteFloat(pos.z);
            w.WriteFloat(yaw);
            w.End();
        }

        public void SendMyTransform(int netId, Vector3 pos, float yaw)
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return;

            WriteTransform(netId, pos, yaw);

            if (net.IsHost)
                net.Host.Broadcast(w);
            else if (net.Client != null)
                net.Client.Send(w);
        }

        private Vector3 PickSpawnPos(int netId)
        {
            float angle = netId * 137.5f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * spawnRadius, 1f, Mathf.Sin(angle) * spawnRadius);
        }
    }
}
