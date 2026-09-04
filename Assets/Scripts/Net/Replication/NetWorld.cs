using System;
using System.Collections;
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

        private const int MAX_NAME_LENGTH = 16;

        private readonly List<int> removedSceneIds = new List<int>();

        public IReadOnlyDictionary<int, NetIdentity> Objects { get { return objects; } }

        public event Action<NetIdentity> OnSpawned;
        public event Action<int> OnDespawned;

        public NetIdentity Find(int netId)
        {
            NetIdentity id;
            return objects.TryGetValue(netId, out id) ? id : null;
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
            net.OnPeerLeft += HandlePeerLeft;
            net.OnDisconnected += ClearAll;

            RegisterRoutes(net);

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

                //씬 배치물: netId는 고정 씬 ID, 주인은 없음(0), 프리팹 번호가 비었으면 젤리로 본다
                id.Assign(id.SceneNetId, 0, id.PrefabId == 0 ? NetConfig.JELLY_PREFAB_START : id.PrefabId);

                objects[id.NetId] = id;
                OnSpawned?.Invoke(id);
                n++;
            }

            if (n > 0)
                Debug.Log("[NetWorld] 씬 배치 오브젝트 " + n + "개 등록");
            else
                Debug.LogWarning("[NetWorld] 씬 배치 오브젝트가 하나도 등록되지 않았습니다. ");
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

                if (prefabs[i].GetComponent<NetTransform>() == null)
                    Debug.LogError("[NetWorld] '" + prefabs[i].name + "' 에 NetTransform이 없습니다 — 위치 동기화가 전혀 안 됩니다!");
            }
        }

        private void OnDestroy()
        {
            pool?.Clear();

            NetManager net = NetManager.Instance;
            if (net == null)
                return;
            net.OnPeerLeft -= HandlePeerLeft;
            net.OnDisconnected -= ClearAll;

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

        //스폰 시점은 소켓 연결이 아니라 클라의 SceneReady 하나뿐이다.
        //연결되자마자 스냅샷을 쏘면 클라는 아직 로비라 NetWorld가 없어 그대로 버려지고,
        //나중에 SceneReady로 한 번 더 스폰돼 캐릭터가 둘 생긴다.
        //LAN은 한 판의 인원이 로비에서 확정되므로 도중 난입 경로는 아예 두지 않는다.
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

        //클라가 보낸 메시지는 전부 "주장"이다. 어느 경로든 소유권 확인을 통과해야 반영된다.
        //from(보낸 사람 번호)은 전송 계층이 붙이는 값이라 위조할 수 없고, 메시지 본문의 netId는 위조할 수 있다
        private void RegisterRoutes(NetManager net)
        {
            // ── 호스트가 받는 것 (클라의 '주장') ──
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
                    //거를 것이 있어도 항목의 바이트는 먼저 전부 읽는다.
                    //중간에서 빠져나가면 뒤 항목이 한 칸씩 밀려 쓰레기를 읽는다
                    int netId = r.ReadInt();
                    float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat(), yaw = r.ReadFloat();
                    float sendTime = r.ReadFloat();

                    NetIdentity id;
                    if (!objects.TryGetValue(netId, out id))
                        continue;

                    //이게 없으면 남의 netId로 위치를 보내 순간이동시킬 수 있다
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

                //보낸 사람은 이미 자기 화면에서 움직였다. 되돌려주면 과거 좌표로 끌려간다
                //sendTime은 원본 그대로 넘긴다. 여기서 다시 찍으면 호스트 처리 지연이
                //그대로 타임라인에 섞여 버벅임이 남는다
                WriteTransforms(relay);
                NetManager.Instance.BroadcastExcept(from, w);
            });

            // ── 클라가 받는 것 (호스트의 '확정') ──
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

        //이름은 사람 캐릭터 하나에만 붙인다. break가 없으면 그 사람 소유의 봇까지 같은 이름이 된다
        private void HostApplyName(int from, string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            //클라가 보낸 문자열은 길이를 믿을 수 없다. UI가 무너지고 스냅샷마다 실려 나간다
            if (name.Length > MAX_NAME_LENGTH)
                name = name.Substring(0, MAX_NAME_LENGTH);

            //objects는 씬 사탕까지 포함해 300개가 넘는다. 사람 캐릭터를 찾자고 전부 돌 이유가 없다
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

            id.Assign(netId, ownerId, prefabId);

            //EnsurePlayerComponents가 방금 붙였을 수 있다. Awake는 그 전에 돌았다
            id.RefreshComponentCache();

            LanPlayerSetup setup = id.GetComponent<LanPlayerSetup>();
            if (setup != null)
                setup.Apply();

            objects[netId] = id;
            NetManager.Instance.AddLog("스폰: net" + netId + " (프리팹 " + prefabId + ", 소유 P" + ownerId + ")");

            OnSpawned?.Invoke(id);
            return id;
        }

        private static void EnsurePlayerComponents(GameObject go)
        {
            if (go.GetComponentInChildren<PlayerMovement>(true) == null)
                return;

            if (go.GetComponent<LanPlayerSetup>() == null)
            {
                go.AddComponent<LanPlayerSetup>();
                Debug.LogWarning("[NetWorld] " + go.name + " 에 LanPlayerSetup이 없어 런타임에 추가했습니다.");
            }
            if (go.GetComponent<LanPlayerVisual>() == null)
            {
                go.AddComponent<LanPlayerVisual>();
                Debug.LogWarning("[NetWorld] " + go.name + " 에 LanPlayerVisual이 없어 런타임에 추가했습니다.");
            }
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
                //씬에 미리 배치된 오브젝트는 우리가 만든 게 아니라 풀에 넣을 수 없다
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

        //sendTime은 '보낸 사람의 시계'다. 중계할 때 호스트가 다시 찍으면 안 된다.
        //받는 쪽은 이 값으로 보간 타임라인을 세운다 — 도착 시각을 쓰면 네트워크 지터가
        //그대로 속도 변화로 보인다 (원격 캐릭터가 순간적으로 빨라졌다 느려짐)
        // ═══════════════════════════════════════════════════════
        //  위치 동기화 — [count][entry]*
        // ═══════════════════════════════════════════════════════
        //
        // ★ 왜 묶을 수 있게 해뒀는데 꺼놨나
        //   Photon 무료 티어는 방 하나에 초당 500 메시지다. 지금 호스트가 내보내는 양이
        //   봇 9 + 클라 3 기준으로 초당 약 305개라 여유가 거의 없다. 위치 갱신을 묶으면
        //   그 수가 크게 준다 — 온라인 모드에 반드시 필요해질 손잡이다.
        //
        //   그런데 묶으면 한 메시지가 여러 개를 들고 다니므로, 마지막 것이 나갈 때까지
        //   앞의 것이 프레임 끝까지 기다린다. LAN 은 메시지 수가 문제가 아니라
        //   그 지연만 손해다. 그래서 지금은 꺼둔 채로 두고, 켜는 판단은 온라인이
        //   실제로 붙은 뒤에 수치를 보고 한다.
        //
        //   끈 상태의 동작은 예전과 같다 — 한 번에 하나씩, 부르는 즉시 나간다.
        //   (형식에 [count] 한 바이트가 붙은 것만 다르다. 호스트와 클라는 언제나
        //    같은 빌드라 형식이 어긋날 일은 없다)
        [Header("위치 동기화")]
        [Tooltip("여러 위치 갱신을 한 메시지로 묶어 보낸다. 온라인 모드용 손잡이 — LAN에서는 꺼둔다.")]
        [SerializeField] private bool batchTransforms = false;

        //한 메시지에 담는 최대 개수. count가 1바이트라 255가 상한이고,
        //그 전에 한 프레임에 이만큼 쌓일 일이 없어 넉넉히 32로 둔다
        private const int MAX_TRANSFORM_BATCH = 32;

        private struct TransformEntry
        {
            public int NetId;
            public Vector3 Pos;
            public float Yaw;
            public float SendTime;
        }

        //내보낼 것을 모아두는 곳과, 호스트가 중계할 것을 모아두는 곳.
        //매번 새로 만들면 초당 수백 개의 쓰레기가 된다
        private readonly List<TransformEntry> pending = new List<TransformEntry>();
        private readonly List<TransformEntry> relay = new List<TransformEntry>();

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

            //sendTime은 '보낸 순간'이 아니라 '이 좌표를 잰 순간'이다. 묶어서 나중에 내보내도
            //여기서 찍은 값을 그대로 들고 간다 — 나갈 때 다시 찍으면 묶느라 생긴 지연이
            //그대로 타임라인에 섞여 받는 쪽이 버벅인다.
            //timeScale에 흔들리지 않도록 unscaled를 쓴다. 받는 쪽도 unscaled로 읽는다
            pending.Add(new TransformEntry
            {
                NetId = netId,
                Pos = pos,
                Yaw = yaw,
                SendTime = Time.unscaledTime
            });

            if (!batchTransforms || pending.Count >= MAX_TRANSFORM_BATCH)
                FlushTransforms();
        }

        //묶기를 켰을 때 프레임에 남은 것을 내보낸다. 꺼져 있으면 pending이 늘 비어 있어
        //아무 일도 하지 않는다
        private void LateUpdate()
        {
            if (batchTransforms)
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
