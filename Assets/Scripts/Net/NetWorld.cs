using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 네트워크 오브젝트 세계를 관리한다. Photon의 Instantiate / Destroy / 룸 오브젝트 개념을 대신.
    ///
    /// 역할:
    ///   · 프리팹 등록표(prefabId ↔ 프리팹)
    ///   · netId 발급(호스트만)과 오브젝트 목록 관리
    ///   · 스폰/디스폰을 전 클라에 복제
    ///   · 늦게 들어온 클라에게 기존 오브젝트를 몰아서 전송(스냅샷)
    ///   · 위치 메시지 중계
    ///
    /// ★ 권위 규칙: 생성·파괴·netId 발급은 오직 호스트. 클라는 통보받아 따라 만든다.
    /// </summary>
    public class NetWorld : MonoBehaviour
    {
        public static NetWorld Instance { get; private set; }

        [Header("프리팹 등록표 (배열 인덱스 = prefabId)")]
        [Tooltip("0번은 플레이어 캡슐. 순서가 곧 ID이므로 중간에 끼워넣지 말 것.")]
        public GameObject[] prefabs;

        [Header("스폰 위치")]
        public float spawnRadius = 4f;

        /// <summary>netId → 오브젝트. 메시지가 올 때 대상을 찾는 표.</summary>
        readonly Dictionary<int, NetIdentity> _objects = new Dictionary<int, NetIdentity>();

        readonly NetWriter _w = new NetWriter();
        int _nextNetId = 1;          // 호스트만 사용

        public IReadOnlyDictionary<int, NetIdentity> Objects { get { return _objects; } }

        // ─────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // Start()에서 구독하는 이유: Unity는 모든 Awake()를 먼저 실행하므로
        // 이 시점에는 NetManager.Instance가 확실히 준비되어 있다.
        void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null) { Debug.LogError("[NetWorld] NetManager가 없습니다."); return; }
            net.OnHostStarted += HandleHostStarted;
            net.OnPeerJoined += HandlePeerJoined;
            net.OnPeerLeft += HandlePeerLeft;
            net.OnHostMessage += HandleHostMessage;
            net.OnClientMessage += HandleClientMessage;
            net.OnDisconnected += ClearAll;

            ValidatePrefabs();
        }

        /// <summary>프리팹에 필요한 컴포넌트가 다 붙었는지 미리 확인한다(흔한 설정 실수 방지).</summary>
        void ValidatePrefabs()
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

        void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;
            net.OnHostStarted -= HandleHostStarted;
            net.OnPeerJoined -= HandlePeerJoined;
            net.OnPeerLeft -= HandlePeerLeft;
            net.OnHostMessage -= HandleHostMessage;
            net.OnClientMessage -= HandleClientMessage;
            net.OnDisconnected -= ClearAll;
        }

        // ═════════════════════════════════════════════
        //  호스트 측
        // ═════════════════════════════════════════════

        /// <summary>호스트가 켜지면 자기 캐릭터부터 만든다.</summary>
        void HandleHostStarted()
        {
            SpawnForOwner(NetHost.HostId);
        }

        /// <summary>새 클라 접속 — 기존 것들을 보내주고, 그 사람 캐릭터를 만들어 전원에 알린다.</summary>
        void HandlePeerJoined(NetHost.Peer peer)
        {
            // ① 늦은 입장 스냅샷: 이미 있는 오브젝트를 전부 새 클라에게 알려준다.
            //    이게 없으면 중간에 들어온 사람은 빈 월드를 보게 된다.
            foreach (var kv in _objects)
            {
                NetIdentity id = kv.Value;
                WriteSpawn(id.NetId, id.PrefabId, id.OwnerId, id.transform.position);
                NetManager.Instance.Host.SendTo(peer, _w);
            }

            // ② 새 플레이어의 캐릭터 생성 → 전원에게 복제
            SpawnForOwner(peer.Id);
        }

        /// <summary>클라 퇴장 — 그 사람 소유 오브젝트를 전부 정리한다.</summary>
        void HandlePeerLeft(NetHost.Peer peer)
        {
            List<int> toRemove = new List<int>();
            foreach (var kv in _objects)
                if (kv.Value.OwnerId == peer.Id) toRemove.Add(kv.Key);

            for (int i = 0; i < toRemove.Count; i++)
                HostDespawn(toRemove[i]);
        }

        /// <summary>호스트 전용: 오브젝트를 만들고 전원에게 복제 지시.</summary>
        public NetIdentity SpawnForOwner(int ownerId, int prefabId = 0)
        {
            if (!NetManager.Instance.IsHost) return null;

            int netId = _nextNetId++;
            Vector3 pos = PickSpawnPos(netId);

            NetIdentity id = SpawnLocal(netId, prefabId, ownerId, pos);

            WriteSpawn(netId, prefabId, ownerId, pos);
            NetManager.Instance.Host.Broadcast(_w);
            return id;
        }

        /// <summary>호스트 전용: 오브젝트 파괴 + 전원에게 통보.</summary>
        public void HostDespawn(int netId)
        {
            if (!NetManager.Instance.IsHost) return;

            DespawnLocal(netId);

            _w.Begin(MsgType.DespawnEntity);
            _w.WriteInt(netId);
            _w.End();
            NetManager.Instance.Host.Broadcast(_w);
        }

        /// <summary>클라가 보낸 메시지 처리(호스트 입장).</summary>
        void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type != MsgType.TransformUpdate) return;

            int netId = r.ReadInt();
            float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat(), yaw = r.ReadFloat();

            NetIdentity id;
            if (!_objects.TryGetValue(netId, out id)) return;   // 이미 사라진 오브젝트

            // ★ 권위 검사: 남의 오브젝트를 움직이려는 요청은 무시한다.
            if (id.OwnerId != from.Id) return;

            ApplyTransform(id, new Vector3(x, y, z), yaw);

            // 보낸 사람 말고 나머지에게 중계 (자기 위치를 되돌려받을 필요 없음)
            WriteTransform(netId, new Vector3(x, y, z), yaw);
            NetManager.Instance.Host.BroadcastExcept(from, _w);
        }

        // ═════════════════════════════════════════════
        //  클라이언트 측
        // ═════════════════════════════════════════════
        void HandleClientMessage(MsgType type, NetReader r)
        {
            switch (type)
            {
                case MsgType.SpawnEntity:
                    {
                        int netId = r.ReadInt();
                        int prefabId = r.ReadInt();
                        int ownerId = r.ReadInt();
                        float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat();
                        SpawnLocal(netId, prefabId, ownerId, new Vector3(x, y, z));
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
                        if (_objects.TryGetValue(netId, out id))
                            ApplyTransform(id, new Vector3(x, y, z), yaw);
                        break;
                    }
            }
        }

        // ═════════════════════════════════════════════
        //  공통: 실제 생성/파괴
        // ═════════════════════════════════════════════
        NetIdentity SpawnLocal(int netId, int prefabId, int ownerId, Vector3 pos)
        {
            if (_objects.ContainsKey(netId)) return _objects[netId];   // 중복 방지(멱등)

            if (prefabs == null || prefabId < 0 || prefabId >= prefabs.Length || prefabs[prefabId] == null)
            {
                Debug.LogError("[NetWorld] prefabId " + prefabId + " 에 해당하는 프리팹이 없습니다.");
                return null;
            }

            GameObject go = Instantiate(prefabs[prefabId], pos, Quaternion.identity);
            go.name = prefabs[prefabId].name + "_net" + netId + "_own" + ownerId;

            NetIdentity id = go.GetComponent<NetIdentity>();
            if (id == null) id = go.AddComponent<NetIdentity>();

            id.NetId = netId;
            id.OwnerId = ownerId;
            id.PrefabId = prefabId;

            _objects[netId] = id;
            NetManager.Instance.AddLog("스폰: net" + netId + " (소유 P" + ownerId + ")");
            return id;
        }

        void DespawnLocal(int netId)
        {
            NetIdentity id;
            if (!_objects.TryGetValue(netId, out id)) return;   // 이미 없음(멱등)

            _objects.Remove(netId);
            if (id != null) Destroy(id.gameObject);
            NetManager.Instance.AddLog("디스폰: net" + netId);
        }

        void ApplyTransform(NetIdentity id, Vector3 pos, float yaw)
        {
            NetTransform nt = id.GetComponent<NetTransform>();
            if (nt != null) nt.OnRemoteTransform(pos, yaw);
            else { id.transform.position = pos; id.transform.rotation = Quaternion.Euler(0, yaw, 0); }
        }

        public void ClearAll()
        {
            foreach (var kv in _objects)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _objects.Clear();
            _nextNetId = 1;
        }

        // ═════════════════════════════════════════════
        //  메시지 조립 (소유자가 위치를 보낼 때도 씀)
        // ═════════════════════════════════════════════
        void WriteSpawn(int netId, int prefabId, int ownerId, Vector3 pos)
        {
            _w.Begin(MsgType.SpawnEntity);
            _w.WriteInt(netId);
            _w.WriteInt(prefabId);
            _w.WriteInt(ownerId);
            _w.WriteFloat(pos.x); _w.WriteFloat(pos.y); _w.WriteFloat(pos.z);
            _w.End();
        }

        void WriteTransform(int netId, Vector3 pos, float yaw)
        {
            _w.Begin(MsgType.TransformUpdate);
            _w.WriteInt(netId);
            _w.WriteFloat(pos.x); _w.WriteFloat(pos.y); _w.WriteFloat(pos.z);
            _w.WriteFloat(yaw);
            _w.End();
        }

        /// <summary>소유자가 자기 위치를 보낼 때 호출(NetTransform이 씀).</summary>
        public void SendMyTransform(int netId, Vector3 pos, float yaw)
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;

            WriteTransform(netId, pos, yaw);

            if (net.IsHost) net.Host.Broadcast(_w);       // 호스트는 곧바로 전원에게
            else if (net.Client != null) net.Client.Send(_w);   // 클라는 호스트에게만
        }

        Vector3 PickSpawnPos(int netId)
        {
            // 겹치지 않게 원형으로 흩어 놓는다
            float angle = netId * 137.5f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * spawnRadius, 1f, Mathf.Sin(angle) * spawnRadius);
        }
    }
}
