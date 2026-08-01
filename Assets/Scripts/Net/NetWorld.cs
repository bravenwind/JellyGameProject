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

        /// <summary>이미 사라진 씬 오브젝트. 늦게 들어온 사람에게 "이건 없다"고 알려준다.</summary>
        readonly List<int> _removedSceneIds = new List<int>();

        public IReadOnlyDictionary<int, NetIdentity> Objects { get { return _objects; } }

        /// <summary>오브젝트가 생겼을 때 / 사라졌을 때. 젤리 개수 관리 등에 쓴다.</summary>
        public event System.Action<NetIdentity> OnSpawned;
        public event System.Action<int> OnDespawned;

        /// <summary>netId로 오브젝트 찾기. 없으면 null.</summary>
        public NetIdentity Find(int netId)
        {
            NetIdentity id;
            return _objects.TryGetValue(netId, out id) ? id : null;
        }

        static float ScaleOf(NetIdentity id)
        {
            NetScale ns = id.GetComponent<NetScale>();
            return ns != null ? ns.Current : 1f;
        }

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
            RegisterSceneObjects();

            // 로비에서 이미 접속을 끝내고 넘어온 경우를 따라잡는다.
            // (씬 오브젝트 등록이 끝난 뒤여야 스냅샷이 온전하다)
            Start_CatchUpNetwork();
        }

        /// <summary>
        /// 씬에 미리 배치된 네트워크 오브젝트(젤리 등)를 목록에 등록한다.
        ///
        /// ★ 이들은 호스트가 스폰하지 않는다. 양쪽이 각자 씬에서 로드하고,
        ///   에디터에서 부여한 SceneNetId로 서로를 이어준다.
        ///   이게 없으면 씬 젤리는 netId 0이라 흡수 요청이 전부 탈락한다.
        /// </summary>
        void RegisterSceneObjects()
        {
            int n = 0;
            foreach (NetIdentity id in FindObjectsByType<NetIdentity>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (id == null || id.SceneNetId == 0) continue;

                id.NetId = id.SceneNetId;
                id.OwnerId = 0;                       // 씬 오브젝트는 주인이 없다
                if (id.PrefabId == 0) id.PrefabId = NetConfig.JellyPrefabStart;   // 젤리로 취급

                _objects[id.NetId] = id;
                if (OnSpawned != null) OnSpawned(id);
                n++;
            }

            if (n > 0) Debug.Log("[NetWorld] 씬 배치 오브젝트 " + n + "개 등록");
            else Debug.LogWarning("[NetWorld] 씬 배치 오브젝트가 하나도 등록되지 않았습니다. "
                                  + "Tools ▸ LAN 이식 ▸ ⑦ 씬 오브젝트 ID 부여 를 실행하세요.");
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

                // 0번(플레이어)은 움직이므로 NetTransform 필수. 젤리는 제자리라 없어도 된다.
                if (i < NetConfig.JellyPrefabStart && prefabs[i].GetComponent<NetTransform>() == null)
                    Debug.LogError("[NetWorld] '" + prefabs[i].name + "' 에 NetTransform이 없습니다 — 위치 동기화가 전혀 안 됩니다!");

                if (prefabs[i].GetComponent<NetScale>() == null)
                    Debug.LogWarning("[NetWorld] '" + prefabs[i].name + "' 에 NetScale이 없습니다 — 흡수해도 크기가 안 변합니다.");
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

        // ═══════════════════════════════════════════════════════
        //  ★ 스폰의 방아쇠는 '접속'이 아니라 '씬 준비 완료'다
        // ═══════════════════════════════════════════════════════
        //
        //  로비를 붙이기 전에는 게임 씬에서 바로 접속했으므로,
        //  "호스트가 켜졌다 / 누가 들어왔다" 이벤트에 맞춰 캐릭터를 만들면 됐다.
        //
        //  이제는 <b>접속이 Main 씬에서 일어난다.</b> 그 순간 NetWorld는 존재하지도 않는다.
        //  그래서 그 이벤트들을 그대로 두면:
        //    · 호스트 자신의 캐릭터가 안 생긴다 (이벤트를 놓쳤으므로)
        //    · 참가자의 캐릭터도 안 생긴다
        //    · 설령 만든다 해도, 아직 로딩 씬에 있는 참가자에게 SpawnEntity가 도착해
        //      받을 사람이 없어 그냥 버려진다
        //
        //  그래서 각자 게임 씬에 도착해 NetWorld가 살아난 시점을 방아쇠로 삼는다.
        //    호스트 → 도착하면 자기 캐릭터를 만든다
        //    참가자 → 도착했다고 알린다(SceneReady) → 호스트가 그때 만들어 보낸다

        void Start_CatchUpNetwork()
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            if (net.IsHost)
            {
                SpawnForOwner(NetHost.HostId);      // 이미 있으면 아래 가드가 막는다
                return;
            }

            _w.Begin(MsgType.SceneReady);
            _w.End();
            net.Client.Send(_w);
        }

        /// <summary>호스트가 켜지면 자기 캐릭터부터 만든다(게임 씬에서 바로 시작한 경우).</summary>
        void HandleHostStarted()
        {
            SpawnForOwner(NetHost.HostId);
        }

        /// <summary>
        /// 참가자가 게임 씬에 도착했다 — 이제 스냅샷을 보내고 캐릭터를 만들어준다.
        /// 로비에서 접속한 시점에 보내면 아직 받을 NetWorld가 없어 버려진다.
        /// </summary>
        void HandleSceneReady(NetHost.Peer peer)
        {
            SendWorldSnapshot(peer);
            SpawnForOwner(peer.Id);
        }

        /// <summary>새 클라 접속 — 게임 씬에서 바로 붙은 경우의 경로.</summary>
        void HandlePeerJoined(NetHost.Peer peer)
        {
            SendWorldSnapshot(peer);
            SpawnForOwner(peer.Id);
        }

        /// <summary>지금까지의 월드 상태를 한 사람에게 몰아 보낸다.</summary>
        void SendWorldSnapshot(NetHost.Peer peer)
        {
            // ① 늦은 입장 스냅샷: 이미 있는 오브젝트를 전부 새 클라에게 알려준다.
            //    이게 없으면 중간에 들어온 사람은 빈 월드를 보게 된다.
            foreach (var kv in _objects)
            {
                NetIdentity id = kv.Value;

                // 씬 배치 오브젝트는 새 클라도 자기 씬에서 이미 로드했으므로 다시 만들지 않는다
                if (id.NetId >= NetConfig.SceneIdBase) continue;

                // 현재 크기까지 실어 보낸다 → 늦게 들어와도 커진 젤리·플레이어가 제대로 보인다
                WriteSpawn(id.NetId, id.PrefabId, id.OwnerId, id.transform.position, ScaleOf(id));
                NetManager.Instance.Host.SendTo(peer, _w);

                // 점수·색·이름도 이어서 보낸다(스폰 메시지에 다 넣으면 비대해지므로 분리)
                LanPlayerState ps = id.GetComponent<LanPlayerState>();
                if (ps != null)
                {
                    WritePlayerState(id.NetId, ps.Score, (byte)ps.Flags, ps.DisplayColor);
                    NetManager.Instance.Host.SendTo(peer, _w);

                    if (!string.IsNullOrEmpty(ps.PlayerName))
                    {
                        _w.Begin(MsgType.PlayerNameSet);
                        _w.WriteInt(id.NetId);
                        _w.WriteString(ps.PlayerName);
                        _w.End();
                        NetManager.Instance.Host.SendTo(peer, _w);
                    }
                }
            }

            // ★ 이미 먹힌 씬 젤리를 알려준다.
            //   씬 오브젝트는 새 클라도 자기 씬에서 로드하므로, 알려주지 않으면
            //   '이미 없어진 젤리'가 그 사람 화면에만 남는다.
            for (int i = 0; i < _removedSceneIds.Count; i++)
            {
                _w.Begin(MsgType.DespawnEntity);
                _w.WriteInt(_removedSceneIds[i]);
                _w.End();
                NetManager.Instance.Host.SendTo(peer, _w);
            }
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

        /// <summary>호스트 전용: 플레이어 캐릭터를 스폰 포인트에 만든다.</summary>
        public NetIdentity SpawnForOwner(int ownerId, int prefabId = 0)
        {
            if (!NetManager.Instance.IsHost) return null;

            // ★ 이미 그 사람의 캐릭터가 있으면 또 만들지 않는다.
            //   방아쇠가 여러 개다(호스트 시작 · 씬 준비 완료 · 접속). 로비를 거치는
            //   경로와 게임 씬에서 바로 붙는 경로가 섞이면서 같은 사람 것이 두 번
            //   만들어질 수 있는데, 그러면 조종되지 않는 유령 캐릭터가 남는다.
            foreach (var kv in _objects)
            {
                NetIdentity ex = kv.Value;
                if (ex == null || ex.OwnerId != ownerId) continue;
                if (ex.IsBot) continue;                       // 봇도 호스트 소유다
                if (ex.PrefabId != prefabId) continue;
                return ex;
            }

            return HostSpawn(prefabId, ownerId, PickPlayerSpawnPos());
        }

        /// <summary>
        /// 플레이어 스폰 위치. 씬에 스폰포인트가 있으면 그것을 쓰고, 없으면 원형 배치로 폴백.
        /// (원본 NetworkManager가 SpawnPoint 태그를 쓰던 규칙을 그대로 따른다)
        /// </summary>
        Vector3 PickPlayerSpawnPos()
        {
            if (LanSpawnPoints.Instance != null) return LanSpawnPoints.Instance.Take();
            return PickSpawnPos(_nextNetId);
        }

        /// <summary>
        /// 호스트 전용: 원하는 위치에 오브젝트를 만들고 전원에게 복제 지시.
        /// 젤리처럼 소유자가 없는 것은 ownerId = 0.
        /// </summary>
        public NetIdentity HostSpawn(int prefabId, int ownerId, Vector3 pos)
        {
            if (!NetManager.Instance.IsHost) return null;

            int netId = _nextNetId++;
            NetIdentity id = SpawnLocal(netId, prefabId, ownerId, pos, 1f);

            WriteSpawn(netId, prefabId, ownerId, pos, 1f);
            NetManager.Instance.Host.Broadcast(_w);
            return id;
        }

        /// <summary>
        /// 호스트 전용: 성장 사건을 전원에게 알린다.
        /// 각 클라가 기존 PlayerScaleController 함수를 불러 같은 연출을 재생한다.
        /// </summary>
        public void BroadcastGrow(int netId, GrowKind kind, float amount)
        {
            if (!NetManager.Instance.IsHost) return;

            _w.Begin(MsgType.GrowEvent);
            _w.WriteInt(netId);
            _w.WriteByte((byte)kind);
            _w.WriteFloat(amount);
            _w.End();
            NetManager.Instance.Host.Broadcast(_w);

            // 호스트 자신도 적용
            NetIdentity id = Find(netId);
            if (id != null)
            {
                LanPlayerVisual v = id.GetComponent<LanPlayerVisual>();
                if (v != null) v.ApplyGrow(kind, amount);
            }
        }

        /// <summary>클라가 보낸 애니메이션 정보를 나머지에게 중계한다.</summary>
        public void RelayAnimState(NetHost.Peer from, int netId, byte kind, byte value)
        {
            if (!NetManager.Instance.IsHost) return;

            _w.Begin(MsgType.AnimState);
            _w.WriteInt(netId);
            _w.WriteByte(kind);
            _w.WriteByte(value);
            _w.End();
            NetManager.Instance.Host.BroadcastExcept(from, _w);

            NetIdentity id = Find(netId);
            if (id != null && !id.IsMine)
            {
                LanPlayerVisual v = id.GetComponent<LanPlayerVisual>();
                if (v != null) v.ApplyAnim(kind, value);
            }
        }

        /// <summary>호스트 전용: 크기가 바뀌었음을 전원에게 알린다.</summary>
        public void BroadcastScale(int netId, float scale)
        {
            if (!NetManager.Instance.IsHost) return;

            _w.Begin(MsgType.StateUpdate);
            _w.WriteInt(netId);
            _w.WriteFloat(scale);
            _w.End();
            NetManager.Instance.Host.Broadcast(_w);
        }

        /// <summary>
        /// 호스트 전용: 밟아서 무너진 타일을 전원에게 알린다.
        /// 링 단위 붕괴는 시간 기반이라 각자 알아서 무너지지만, 밟은 자리는 호스트만 안다.
        /// </summary>
        public void BroadcastTileCollapse(int x, int z)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost) return;

            _w.Begin(MsgType.TileCollapse);
            _w.WriteInt(x);
            _w.WriteInt(z);
            _w.End();
            net.Host.Broadcast(_w);
        }

        /// <summary>
        /// 호스트 전용: 타일이 몇 번 밟혔는지(= 얼마나 어두워졌는지)를 알린다.
        ///
        /// 밟기 마모는 호스트만 세므로 그 결과를 내려보내야 한다.
        /// 붕괴 직전 단계까지를 다루고, 한계에 닿으면 TileCollapse 쪽으로 넘어간다.
        /// </summary>
        public void BroadcastTileWear(int x, int z, int count, int maxSteps)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost) return;

            _w.Begin(MsgType.TileWear);
            _w.WriteInt(x);
            _w.WriteInt(z);
            _w.WriteByte((byte)Mathf.Clamp(count, 0, 255));
            _w.WriteByte((byte)Mathf.Clamp(maxSteps, 1, 255));
            _w.End();
            net.Host.Broadcast(_w);
        }

        /// <summary>호스트 전용: 점수·탈락·색을 한 번에 알린다.</summary>
        public void BroadcastPlayerState(int netId, int score, byte flags, Color color)
        {
            if (!NetManager.Instance.IsHost) return;

            WritePlayerState(netId, score, flags, color);
            NetManager.Instance.Host.Broadcast(_w);
        }

        /// <summary>호스트 전용: 닉네임을 알린다.</summary>
        public void BroadcastPlayerName(int netId, string name)
        {
            if (!NetManager.Instance.IsHost) return;

            _w.Begin(MsgType.PlayerNameSet);
            _w.WriteInt(netId);
            _w.WriteString(name);
            _w.End();
            NetManager.Instance.Host.Broadcast(_w);
        }

        void WritePlayerState(int netId, int score, byte flags, Color color)
        {
            _w.Begin(MsgType.PlayerStateUpdate);
            _w.WriteInt(netId);
            _w.WriteInt(score);
            _w.WriteByte(flags);
            _w.WriteFloat(color.r);
            _w.WriteFloat(color.g);
            _w.WriteFloat(color.b);
            _w.End();
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
            // ═════════════════════════════════════════════
            //  닉네임 등록
            // ═════════════════════════════════════════════
            //
            // ★ 왜 클라가 보내야 하는가
            //   닉네임은 로비에서 각자 입력한다. 호스트는 그 값을 알 방법이 없다.
            //   그래서 접속한 쪽이 알려주고, 호스트가 확정해서 전원에게 재방송한다.
            //   (LanPlayerState.HostSetName이 호스트 전용인 이유이기도 하다)
            //
            //   자기 오브젝트에만 이름을 붙일 수 있다 — 남의 이름을 바꾸지 못하게.
            if (type == MsgType.SetMyName)
            {
                string name = r.ReadString();
                if (string.IsNullOrEmpty(name)) return;
                if (name.Length > 16) name = name.Substring(0, 16);

                foreach (var kv in _objects)
                {
                    NetIdentity owned = kv.Value;
                    if (owned == null || owned.OwnerId != from.Id) continue;

                    LanPlayerState ps = owned.GetComponent<LanPlayerState>();
                    if (ps == null) continue;

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
                if (a != null && a.OwnerId == from.Id)      // 소유권 검사
                    RelayAnimState(from, aNetId, kind, value);
                return;
            }

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
                            if (ns != null) ns.SetTarget(scale);
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
                            if (ps != null) ps.ApplyState(score, flags, new Color(cr, cg, cb, 1f));
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
                            if (v != null) v.ApplyGrow(kind, amount);
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
                            if (v != null) v.ApplyAnim(kind, value);
                        }
                        break;
                    }

                case MsgType.TileCollapse:
                    {
                        int tx = r.ReadInt();
                        int tz = r.ReadInt();
                        if (TileCollapseManager.Instance != null)
                            TileCollapseManager.Instance.CollapseStepTile(tx, tz, false);   // 재방송 금지
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
                            if (bs != null) bs.ApplyState(s, new Color(cr, cg, cb, 1f));
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
                            if (bot != null) bot.ApplyEliminatedFromNet();
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
                            if (ps != null) ps.ApplyName(name);
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
                        if (_objects.TryGetValue(netId, out id))
                            ApplyTransform(id, new Vector3(x, y, z), yaw);
                        break;
                    }
            }
        }

        // ═════════════════════════════════════════════
        //  공통: 실제 생성/파괴
        // ═════════════════════════════════════════════
        NetIdentity SpawnLocal(int netId, int prefabId, int ownerId, Vector3 pos, float scale)
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

            // ★ 안전망: 플레이어 프리팹에 네트워크 컴포넌트가 빠져 있으면 여기서 붙인다.
            //   프리팹 변환 순서에 따라 누락될 수 있어(실제로 겪음) 런타임에서 한 번 더 보장한다.
            EnsurePlayerComponents(go);

            id.NetId = netId;
            id.OwnerId = ownerId;
            id.PrefabId = prefabId;

            NetScale ns = id.GetComponent<NetScale>();
            if (ns != null) ns.SetImmediate(scale);

            // ★ 소유자가 확정된 지금 로컬/원격 구성을 적용한다.
            //   Awake/Start에서 하면 아직 OwnerId가 0이라 전부 '원격'으로 판정된다.
            LanPlayerSetup setup = id.GetComponent<LanPlayerSetup>();
            if (setup != null) setup.Apply();

            _objects[netId] = id;
            NetManager.Instance.AddLog("스폰: net" + netId + " (프리팹 " + prefabId + ", 소유 P" + ownerId + ")");

            if (OnSpawned != null) OnSpawned(id);
            return id;
        }

        /// <summary>
        /// 조작 가능한 플레이어(PlayerMovement 보유)에 필요한 네트워크 컴포넌트를 보장한다.
        /// 프리팹에 이미 있으면 아무 일도 하지 않는다.
        /// </summary>
        static void EnsurePlayerComponents(GameObject go)
        {
            if (go.GetComponentInChildren<PlayerMovement>(true) == null) return;   // 플레이어가 아님

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
            if (go.GetComponent<LanPlayerState>() == null) go.AddComponent<LanPlayerState>();
            if (go.GetComponent<NetKnockback>() == null) go.AddComponent<NetKnockback>();
        }

        void DespawnLocal(int netId)
        {
            NetIdentity id;
            if (!_objects.TryGetValue(netId, out id)) return;   // 이미 없음(멱등)

            _objects.Remove(netId);
            if (id != null) Destroy(id.gameObject);

            // 씬 오브젝트는 '이미 먹혔다'고 기억해둔다 — 늦게 들어온 사람에게 알려주기 위해
            if (netId >= NetConfig.SceneIdBase) _removedSceneIds.Add(netId);

            if (OnDespawned != null) OnDespawned(netId);
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
            {
                // 씬 배치 오브젝트는 파괴하지 않는다 — 씬을 다시 로드하지 않는 한 그대로 둔다
                if (kv.Key >= NetConfig.SceneIdBase) continue;
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            }
            _objects.Clear();
            _removedSceneIds.Clear();
            _nextNetId = 1;

            RegisterSceneObjects();   // 남아 있는 씬 오브젝트를 다시 등록
        }

        // ═════════════════════════════════════════════
        //  메시지 조립 (소유자가 위치를 보낼 때도 씀)
        // ═════════════════════════════════════════════
        void WriteSpawn(int netId, int prefabId, int ownerId, Vector3 pos, float scale)
        {
            _w.Begin(MsgType.SpawnEntity);
            _w.WriteInt(netId);
            _w.WriteInt(prefabId);
            _w.WriteInt(ownerId);
            _w.WriteFloat(pos.x); _w.WriteFloat(pos.y); _w.WriteFloat(pos.z);
            _w.WriteFloat(scale);
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
