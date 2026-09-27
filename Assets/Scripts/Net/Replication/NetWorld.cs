using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetWorld : MonoBehaviour
    {
        public static NetWorld Instance { get; private set; }

        private readonly Dictionary<int, NetIdentity> objects = new Dictionary<int, NetIdentity>();

        private readonly NetWriter w = new NetWriter();
        private int nextNetId = 1;

        private readonly List<int> removedSceneIds = new List<int>();

        private NetSpawnPool pool;

        public GameObject[] prefabs;

        public float spawnRadius = 4f;

        private const int MAX_NAME_LENGTH = 16;

        public event Action<NetIdentity> OnSpawned;
        public event Action<int> OnDespawned;

        private const int MAX_TRANSFORM_BATCH = 32;

        private readonly List<TransformEntry> pending = new List<TransformEntry>();
        private readonly List<TransformEntry> relay = new List<TransformEntry>();

        public IReadOnlyDictionary<int, NetIdentity> Objects { get { return objects; } }

        public NetIdentity Find(int netId)
        {
            NetIdentity id;
            return objects.TryGetValue(netId, out id) ? id : null;
        }

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
                return;
            net.Events.OnPeerLeft += HandlePeerLeft;
            net.Events.OnDisconnected += ClearAll;

            RegisterRoutes(net);

            RegisterSceneObjects();

            Start_CatchUpNetwork();
        }

        private void RegisterSceneObjects()
        {
            foreach (NetIdentity id in FindObjectsByType<NetIdentity>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (id == null || id.SceneNetId == 0)
                    continue;

                id.Assign(id.SceneNetId, 0, id.PrefabId == 0 ? NetConfig.JELLY_PREFAB_START : id.PrefabId);

                objects[id.NetId] = id;
                OnSpawned?.Invoke(id);
            }
        }

        private void OnDestroy()
        {
            pool?.Clear();

            NetManager net = NetManager.Instance;
            if (net == null)
                return;
            net.Events.OnPeerLeft -= HandlePeerLeft;
            net.Events.OnDisconnected -= ClearAll;

            UnregisterRoutes(net);
        }

        private void Start_CatchUpNetwork()
        {
            NetManager net = NetManager.Instance;
            if (NetManager.Offline)
                return;

            if (net.IsHost)
            {
                net.AcceptingNewPeers = false;

                SpawnForOwner(NetHost.HOST_ID);
                return;
            }

            StartCoroutine(SceneReadyLoop());
        }

        private IEnumerator SceneReadyLoop()
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
                net.SendToHost(w);

                yield return new WaitForSeconds(INTERVAL);
                waited += INTERVAL;
            }
        }

        private bool HasMyObject()
        {
            foreach (var kv in objects)
                if (kv.Value != null && kv.Value.IsMine && !kv.Value.IsBot)
                    return true;
            return false;
        }

        private void HandleSceneReady(int peerId)
        {
            SendWorldSnapshot(peerId);
            SpawnForOwner(peerId);
        }

        private void SendWorldSnapshot(int peerId)
        {
            foreach (var kv in objects)
            {
                NetIdentity id = kv.Value;

                if (id.NetId >= NetConfig.SCENE_ID_BASE)
                    continue;

                WriteSpawn(id.NetId, id.PrefabId, id.OwnerId, id.transform.position);
                NetManager.Instance.SendTo(peerId, w);

                LanPlayerState ps = id.PlayerState;
                if (ps != null)
                {
                    WritePlayerState(id.NetId, ps.Score, (byte)ps.Flags, ps.DisplayColor);
                    NetManager.Instance.SendTo(peerId, w);

                    if (!string.IsNullOrEmpty(ps.PlayerName))
                    {
                        w.Begin(MsgType.PlayerNameSet);
                        w.WriteInt(id.NetId);
                        w.WriteString(ps.PlayerName);
                        w.End();
                        NetManager.Instance.SendTo(peerId, w);
                    }
                }
            }

            for (int i = 0; i < removedSceneIds.Count; i++)
            {
                w.Begin(MsgType.DespawnEntity);
                w.WriteInt(removedSceneIds[i]);
                w.End();
                NetManager.Instance.SendTo(peerId, w);
            }
        }

        private void HandlePeerLeft(int peerId)
        {
            List<int> toRemove = new List<int>();
            foreach (var kv in objects)
                if (kv.Value.OwnerId == peerId)
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
            NetIdentity id = SpawnLocal(netId, prefabId, ownerId, pos);

            WriteSpawn(netId, prefabId, ownerId, pos);
            NetManager.Instance.Broadcast(w);
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
            NetManager.Instance.Broadcast(w);

            NetIdentity id = Find(netId);
            if (id != null)
            {
                LanPlayerVisual v = id.Visual;
                if (v != null)
                    v.ApplyGrow(kind, amount);
            }
        }

        public void RelayAnimState(int from, int netId, byte kind, byte value)
        {
            if (!NetManager.Instance.IsHost)
                return;

            w.Begin(MsgType.AnimState);
            w.WriteInt(netId);
            w.WriteByte(kind);
            w.WriteByte(value);
            w.End();
            NetManager.Instance.BroadcastExcept(from, w);

            NetIdentity id = Find(netId);
            if (id != null && !id.IsMine)
            {
                LanPlayerVisual v = id.Visual;
                if (v != null)
                    v.ApplyAnim(kind, value);
            }
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
            net.Broadcast(w);
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
            net.Broadcast(w);
        }

        public void BroadcastPlayerState(int netId, int score, byte flags, Color color)
        {
            if (!NetManager.Instance.IsHost)
                return;

            WritePlayerState(netId, score, flags, color);
            NetManager.Instance.Broadcast(w);
        }

        public void BroadcastPlayerName(int netId, string name)
        {
            if (!NetManager.Instance.IsHost)
                return;

            w.Begin(MsgType.PlayerNameSet);
            w.WriteInt(netId);
            w.WriteString(name);
            w.End();
            NetManager.Instance.Broadcast(w);
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
            NetManager.Instance.Broadcast(w);
        }

        private void RegisterRoutes(NetManager net)
        {
            net.RouteHost(MsgType.SceneReady, (from, r) => HandleSceneReady(from));

            net.RouteHost(MsgType.SetMyName, (from, r) => HostApplyName(from, r.ReadString()));

            net.RouteHost(MsgType.AnimState, (from, r) =>
            {
                int netId = r.ReadInt();
                byte kind = r.ReadByte();
                byte value = r.ReadByte();

                NetIdentity id = Find(netId);
                if (id != null && id.OwnerId == from)
                    RelayAnimState(from, netId, kind, value);
            });

            net.RouteHost(MsgType.TransformUpdate, (from, r) =>
            {
                int count = r.ReadByte();
                relay.Clear();

                for (int i = 0; i < count; i++)
                {
                    int netId = r.ReadInt();
                    float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat(), yaw = r.ReadFloat();
                    float sendTime = r.ReadFloat();

                    NetIdentity id;
                    if (!objects.TryGetValue(netId, out id))
                        continue;

                    if (id.OwnerId != from)
                        continue;

                    Vector3 pos = new Vector3(x, y, z);
                    ApplyTransform(id, pos, yaw, sendTime);

                    relay.Add(new TransformEntry
                    {
                        NetId = netId,
                        Pos = pos,
                        Yaw = yaw,
                        SendTime = sendTime
                    });
                }

                if (relay.Count == 0)
                    return;

                WriteTransforms(relay);
                NetManager.Instance.BroadcastExcept(from, w);
            });

            net.RouteClient(MsgType.SpawnEntity, r =>
            {
                int netId = r.ReadInt();
                int prefabId = r.ReadInt();
                int ownerId = r.ReadInt();
                float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat();
                SpawnLocal(netId, prefabId, ownerId, new Vector3(x, y, z));
            });

            net.RouteClient(MsgType.PlayerStateUpdate, r =>
            {
                int netId = r.ReadInt();
                int score = r.ReadInt();
                byte flags = r.ReadByte();
                float cr = r.ReadFloat(), cg = r.ReadFloat(), cb = r.ReadFloat();

                NetIdentity id = Find(netId);
                if (id == null)
                    return;

                LanPlayerState ps = id.PlayerState;
                if (ps != null)
                    ps.ApplyState(score, flags, new Color(cr, cg, cb, 1f));
            });

            net.RouteClient(MsgType.GrowEvent, r =>
            {
                int netId = r.ReadInt();
                GrowKind kind = (GrowKind)r.ReadByte();
                float amount = r.ReadFloat();

                NetIdentity id = Find(netId);
                if (id == null)
                    return;

                LanPlayerVisual v = id.Visual;
                if (v != null)
                    v.ApplyGrow(kind, amount);
            });

            net.RouteClient(MsgType.AnimState, r =>
            {
                int netId = r.ReadInt();
                byte kind = r.ReadByte();
                byte value = r.ReadByte();

                NetIdentity id = Find(netId);
                if (id == null || id.IsMine)
                    return;

                LanPlayerVisual v = id.Visual;
                if (v != null)
                    v.ApplyAnim(kind, value);
            });

            net.RouteClient(MsgType.TileCollapse, r =>
            {
                int tx = r.ReadInt();
                int tz = r.ReadInt();
                if (TileCollapseManager.Instance != null)
                    TileCollapseManager.Instance.CollapseStepTile(tx, tz, false);
            });

            net.RouteClient(MsgType.TileWear, r =>
            {
                int tx = r.ReadInt();
                int tz = r.ReadInt();
                int count = r.ReadByte();
                int maxSteps = r.ReadByte();
                if (TileCollapseManager.Instance != null)
                    TileCollapseManager.Instance.DarkenStepTile(tx, tz, count, maxSteps);
            });

            net.RouteClient(MsgType.BotState, r =>
            {
                int netId = r.ReadInt();
                float s = r.ReadFloat();
                float cr = r.ReadFloat();
                float cg = r.ReadFloat();
                float cb = r.ReadFloat();
                int score = r.ReadInt();
                bool eliminated = r.ReadByte() != 0;

                NetIdentity id = Find(netId);
                if (id == null)
                    return;

                LanBotState bs = id.BotState;
                if (bs != null)
                    bs.ApplyState(s, new Color(cr, cg, cb, 1f), score, eliminated);
            });

            net.RouteClient(MsgType.PlayerNameSet, r =>
            {
                int netId = r.ReadInt();
                string name = r.ReadString();

                NetIdentity id = Find(netId);
                if (id == null)
                    return;

                LanPlayerState ps = id.PlayerState;
                if (ps != null)
                    ps.SetName(name);
            });

            net.RouteClient(MsgType.DespawnEntity, r => DespawnLocal(r.ReadInt()));

            net.RouteClient(MsgType.TransformUpdate, r =>
            {
                int count = r.ReadByte();

                for (int i = 0; i < count; i++)
                {
                    int netId = r.ReadInt();
                    float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat(), yaw = r.ReadFloat();
                    float sendTime = r.ReadFloat();

                    NetIdentity id;
                    if (objects.TryGetValue(netId, out id))
                        ApplyTransform(id, new Vector3(x, y, z), yaw, sendTime);
                }
            });
        }

        private void UnregisterRoutes(NetManager net)
        {
            net.UnrouteHost(MsgType.SceneReady);
            net.UnrouteHost(MsgType.SetMyName);
            net.UnrouteHost(MsgType.AnimState);
            net.UnrouteHost(MsgType.TransformUpdate);

            net.UnrouteClient(MsgType.SpawnEntity);
            net.UnrouteClient(MsgType.PlayerStateUpdate);
            net.UnrouteClient(MsgType.GrowEvent);
            net.UnrouteClient(MsgType.AnimState);
            net.UnrouteClient(MsgType.TileCollapse);
            net.UnrouteClient(MsgType.TileWear);
            net.UnrouteClient(MsgType.BotState);
            net.UnrouteClient(MsgType.PlayerNameSet);
            net.UnrouteClient(MsgType.DespawnEntity);
            net.UnrouteClient(MsgType.TransformUpdate);
        }

        private void HostApplyName(int from, string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (name.Length > MAX_NAME_LENGTH)
                name = name.Substring(0, MAX_NAME_LENGTH);

            IReadOnlyList<LanPlayerState> players = EntityRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                LanPlayerState ps = players[i];
                if (ps == null || ps.OwnerId != from)
                    continue;

                ps.HostSetName(name);
                return;
            }
        }

        private NetIdentity SpawnLocal(int netId, int prefabId, int ownerId, Vector3 pos)
        {
            if (objects.ContainsKey(netId))
                return objects[netId];

            if (prefabs == null || prefabId < 0 || prefabId >= prefabs.Length || prefabs[prefabId] == null)
                return null;

            GameObject go = Pool.Get(prefabId, pos);
            go.name = prefabs[prefabId].name + "_net" + netId + "_own" + ownerId;

            NetIdentity id = go.GetComponent<NetIdentity>();
            if (id == null)
                id = go.AddComponent<NetIdentity>();

            EnsurePlayerComponents(go);

            id.Assign(netId, ownerId, prefabId);

            id.RefreshComponentCache();

            LanPlayerSetup setup = id.GetComponent<LanPlayerSetup>();
            if (setup != null)
                setup.Apply();

            objects[netId] = id;

            OnSpawned?.Invoke(id);
            return id;
        }

        private static void EnsurePlayerComponents(GameObject go)
        {
            if (go.GetComponentInChildren<PlayerMovement>(true) == null)
                return;

            if (go.GetComponent<LanPlayerSetup>() == null)
                go.AddComponent<LanPlayerSetup>();
            if (go.GetComponent<LanPlayerVisual>() == null)
                go.AddComponent<LanPlayerVisual>();
            if (go.GetComponent<LanPlayerState>() == null)
                go.AddComponent<LanPlayerState>();
        }

        private void DespawnLocal(int netId)
        {
            NetIdentity id;
            if (!objects.TryGetValue(netId, out id))
                return;

            objects.Remove(netId);

            if (id != null)
            {
                if (netId >= NetConfig.SCENE_ID_BASE)
                    Destroy(id.gameObject);
                else
                    Pool.Release(id.gameObject);
            }

            if (netId >= NetConfig.SCENE_ID_BASE)
                removedSceneIds.Add(netId);

            OnDespawned?.Invoke(netId);
        }

        private void ApplyTransform(NetIdentity id, Vector3 pos, float yaw, float sendTime)
        {
            NetTransform nt = id.GetComponent<NetTransform>();
            if (nt != null)
                nt.OnRemoteTransform(pos, yaw, sendTime);
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

        private void WriteSpawn(int netId, int prefabId, int ownerId, Vector3 pos)
        {
            w.Begin(MsgType.SpawnEntity);
            w.WriteInt(netId);
            w.WriteInt(prefabId);
            w.WriteInt(ownerId);
            w.WriteFloat(pos.x); w.WriteFloat(pos.y); w.WriteFloat(pos.z);
            w.End();
        }

        private bool BatchTransforms
        {
            get
            {
                NetManager net = NetManager.Instance;
                return net != null && net.PrefersBatchedUpdates;
            }
        }

        private struct TransformEntry
        {
            public int NetId;
            public Vector3 Pos;
            public float Yaw;
            public float SendTime;
        }

        private void WriteTransforms(List<TransformEntry> entries)
        {
            w.Begin(MsgType.TransformUpdate);
            w.WriteByte((byte)entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                TransformEntry e = entries[i];
                w.WriteInt(e.NetId);
                w.WriteFloat(e.Pos.x); w.WriteFloat(e.Pos.y); w.WriteFloat(e.Pos.z);
                w.WriteFloat(e.Yaw);
                w.WriteFloat(e.SendTime);
            }

            w.End();
        }

        public void SendMyTransform(int netId, Vector3 pos, float yaw)
        {
            if (NetManager.Instance == null)
                return;

            pending.Add(new TransformEntry
            {
                NetId = netId,
                Pos = pos,
                Yaw = yaw,
                SendTime = Time.unscaledTime
            });

            if (!BatchTransforms || pending.Count >= MAX_TRANSFORM_BATCH)
                FlushTransforms();
        }

        private void LateUpdate()
        {
            if (BatchTransforms)
                FlushTransforms();
        }

        private void FlushTransforms()
        {
            if (pending.Count == 0)
                return;

            NetManager net = NetManager.Instance;
            if (net == null)
            {
                pending.Clear();
                return;
            }

            WriteTransforms(pending);

            if (net.IsHost)
                net.Broadcast(w);
            else
                net.SendToHost(w);

            pending.Clear();
        }

        private Vector3 PickSpawnPos(int netId)
        {
            float angle = netId * 137.5f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * spawnRadius, 1f, Mathf.Sin(angle) * spawnRadius);
        }
    }
}
