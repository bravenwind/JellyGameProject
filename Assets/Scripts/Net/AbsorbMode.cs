using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 흡수 모드 규칙. Photon판 NetworkJellyManager의 자리를 대신한다.
    ///
    /// 역할이 두 쪽으로 갈린다:
    ///   호스트 — 젤리를 주기적으로 스폰하고, 먹기 요청을 판정한다(권위)
    ///   전원   — 내 캐릭터가 젤리에 닿으면 "먹었다"고 요청만 한다
    ///
    /// ★ 권위 패턴 (Photon판과 동일)
    ///     클라: "이 젤리 먹었어요" 요청
    ///     호스트: 선착 1명만 인정 → 젤리 제거 + 보상 확정 방송
    ///     전원: 결과를 통보받아 반영
    ///
    ///   클라가 스스로 젤리를 지우거나 크게 만들지 않는다.
    ///   그래야 두 명이 동시에 같은 젤리를 먹어도 한 명만 성장한다.
    /// </summary>
    public class AbsorbMode : MonoBehaviour
    {
        public static AbsorbMode Instance { get; private set; }

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
        [Tooltip("젤리 하나당 커지는 배율")]
        public float growPerJelly = 0.12f;

        readonly NetWriter _w = new NetWriter();

        /// <summary>지금 살아있는 젤리들의 netId (스폰/디스폰 이벤트로 유지).</summary>
        readonly HashSet<int> _jellies = new HashSet<int>();

        /// <summary>이미 요청을 보낸 젤리 — 왕복 대기 중 중복 요청을 막는다.</summary>
        readonly HashSet<int> _requested = new HashSet<int>();

        float _spawnTimer;
        NetIdentity _myPlayer;

        public int JellyCount { get { return _jellies.Count; } }

        // ─────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null) { Debug.LogError("[AbsorbMode] NetManager가 없습니다."); return; }

            net.OnHostMessage += HandleHostMessage;
            net.OnClientMessage += HandleClientMessage;
            net.OnDisconnected += ResetAll;

            if (NetWorld.Instance != null)
            {
                NetWorld.Instance.OnSpawned += HandleSpawned;
                NetWorld.Instance.OnDespawned += HandleDespawned;
            }
        }

        void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net != null)
            {
                net.OnHostMessage -= HandleHostMessage;
                net.OnClientMessage -= HandleClientMessage;
                net.OnDisconnected -= ResetAll;
            }
            if (NetWorld.Instance != null)
            {
                NetWorld.Instance.OnSpawned -= HandleSpawned;
                NetWorld.Instance.OnDespawned -= HandleDespawned;
            }
        }

        void ResetAll()
        {
            _jellies.Clear();
            _requested.Clear();
            _myPlayer = null;
            _spawnTimer = 0f;
        }

        // ─────────────────────────────────────────────
        //  젤리 목록 유지
        // ─────────────────────────────────────────────
        void HandleSpawned(NetIdentity id)
        {
            if (IsJelly(id)) _jellies.Add(id.NetId);
            else if (id.IsMine) _myPlayer = id;      // 내 캐릭터 기억
        }

        void HandleDespawned(int netId)
        {
            _jellies.Remove(netId);
            _requested.Remove(netId);
            if (_myPlayer != null && _myPlayer.NetId == netId) _myPlayer = null;
        }

        static bool IsJelly(NetIdentity id)
        {
            return id.PrefabId >= NetConfig.JellyPrefabStart;
        }

        // ─────────────────────────────────────────────
        void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            if (net.IsHost) HostSpawnTick();
            CheckMyEating();
        }

        // ═════════════════════════════════════════════
        //  호스트: 젤리 스폰
        // ═════════════════════════════════════════════
        void HostSpawnTick()
        {
            if (!spawnJelly || NetWorld.Instance == null) return;

            _spawnTimer += Time.deltaTime;
            if (_spawnTimer < spawnInterval) return;
            _spawnTimer -= spawnInterval;

            if (_jellies.Count >= maxJellyCount) return;

            GameObject[] prefabs = NetWorld.Instance.prefabs;
            if (prefabs == null || prefabs.Length <= NetConfig.JellyPrefabStart)
            {
                Debug.LogWarning("[AbsorbMode] 젤리 프리팹이 등록되지 않았습니다 (Prefabs 1번 이후).");
                spawnJelly = false;   // 로그 도배 방지
                return;
            }

            int prefabId = Random.Range(NetConfig.JellyPrefabStart, prefabs.Length);
            Vector3 pos = new Vector3(
                Random.Range(-spawnRangeX, spawnRangeX),
                spawnHeight,
                Random.Range(-spawnRangeZ, spawnRangeZ));

            NetWorld.Instance.HostSpawn(prefabId, 0, pos);   // ownerId 0 = 주인 없음
        }

        // ═════════════════════════════════════════════
        //  전원: 내 캐릭터가 젤리에 닿았는지 검사 → 요청
        // ═════════════════════════════════════════════
        void CheckMyEating()
        {
            if (_myPlayer == null) { _myPlayer = FindMyPlayer(); if (_myPlayer == null) return; }
            if (NetWorld.Instance == null) return;

            NetScale myScale = _myPlayer.GetComponent<NetScale>();
            float myR = playerRadius * (myScale != null ? myScale.Current : 1f);
            Vector3 myPos = _myPlayer.transform.position;

            // ★ 먼저 대상만 고르고, 요청은 반복문 밖에서 한다.
            //   RequestEat이 호스트에서는 즉시 판정 → 젤리 제거 → _jellies 변경으로 이어지므로,
            //   반복문 안에서 호출하면 '열거 중 컬렉션 수정'이 된다.
            int target = -1;
            float reach = (myR + jellyRadius) * (myR + jellyRadius);

            foreach (int jellyId in _jellies)
            {
                if (_requested.Contains(jellyId)) continue;    // 이미 요청함(응답 대기)

                NetIdentity jelly = NetWorld.Instance.Find(jellyId);
                if (jelly == null) continue;

                // 수평 거리만 본다(높이 차이는 무시)
                Vector3 d = jelly.transform.position - myPos;
                d.y = 0f;
                if (d.sqrMagnitude > reach) continue;

                target = jellyId;
                break;
            }

            if (target >= 0) RequestEat(target, _myPlayer.NetId);
        }

        NetIdentity FindMyPlayer()
        {
            if (NetWorld.Instance == null) return null;
            foreach (var kv in NetWorld.Instance.Objects)
                if (!IsJelly(kv.Value) && kv.Value.IsMine) return kv.Value;
            return null;
        }

        /// <summary>"이 젤리 먹었다"고 호스트에 요청. 판정은 호스트가 한다.</summary>
        public void RequestEat(int jellyNetId, int eaterNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;

            _requested.Add(jellyNetId);

            if (net.IsHost)
            {
                // 호스트는 자기 자신에게 보낼 필요 없이 바로 판정
                ResolveEat(NetHost.HostId, jellyNetId, eaterNetId);
                return;
            }

            _w.Begin(MsgType.EatJellyRequest);
            _w.WriteInt(jellyNetId);
            _w.WriteInt(eaterNetId);
            _w.End();
            net.Client.Send(_w);
        }

        // ═════════════════════════════════════════════
        //  호스트: 먹기 판정 (권위)
        // ═════════════════════════════════════════════
        void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type != MsgType.EatJellyRequest) return;

            int jellyNetId = r.ReadInt();
            int eaterNetId = r.ReadInt();
            ResolveEat(from.Id, jellyNetId, eaterNetId);
        }

        /// <summary>
        /// 선착 1명만 인정한다.
        ///
        /// ★ 이중 흡수가 원천 차단되는 이유
        ///   판정 도중에 젤리를 즉시 제거하므로, 같은 프레임에 온 두 번째 요청은
        ///   Find()에서 null을 받아 그대로 탈락한다. 별도의 '선점 목록'이 필요 없다.
        ///   (Photon판은 Destroy가 즉시가 아니라 _claimedJellies 집합이 필요했다)
        /// </summary>
        void ResolveEat(int requesterId, int jellyNetId, int eaterNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null) return;

            NetIdentity jelly = NetWorld.Instance.Find(jellyNetId);
            if (jelly == null) return;              // 이미 다른 사람이 먹음 → 탈락
            if (!IsJelly(jelly)) return;            // 젤리가 아닌 걸 먹으려는 요청

            NetIdentity eater = NetWorld.Instance.Find(eaterNetId);
            if (eater == null) return;

            // ★ 소유권 검사: 남의 캐릭터를 내세워 먹으려는 요청은 무시
            if (eater.OwnerId != requesterId) return;

            // 거리 검사: 너무 멀면 무시 (치트/지연 방어). 지연을 감안해 넉넉히 잡는다.
            NetScale es = eater.GetComponent<NetScale>();
            float eaterR = playerRadius * (es != null ? es.Current : 1f);
            Vector3 gap = jelly.transform.position - eater.transform.position;
            gap.y = 0f;
            float allow = (eaterR + jellyRadius) * 2.5f;
            if (gap.sqrMagnitude > allow * allow) return;

            int jellyPrefabId = jelly.PrefabId;

            // ① 젤리를 먼저 없앤다 → 이 시점부터 후발 요청은 자동 탈락
            NetWorld.Instance.HostDespawn(jellyNetId);

            // ② 보상: 크기 증가 (호스트가 계산하고 StateUpdate로 전원에 방송)
            if (es != null) es.HostGrow(growPerJelly);

            // ③ 확정 통보 (점수·연출용 훅. 지금은 로그만)
            _w.Begin(MsgType.EatJellyConfirm);
            _w.WriteInt(eaterNetId);
            _w.WriteInt(jellyPrefabId);
            _w.End();
            net.Host.Broadcast(_w);

            OnEatConfirmed(eaterNetId, jellyPrefabId);   // 호스트 자신도 반영
        }

        // ═════════════════════════════════════════════
        //  클라이언트: 확정 결과 수신
        // ═════════════════════════════════════════════
        void HandleClientMessage(MsgType type, NetReader r)
        {
            if (type != MsgType.EatJellyConfirm) return;

            int eaterNetId = r.ReadInt();
            int jellyPrefabId = r.ReadInt();
            OnEatConfirmed(eaterNetId, jellyPrefabId);
        }

        /// <summary>흡수 확정. 나중에 점수·색·이펙트를 여기에 붙인다.</summary>
        void OnEatConfirmed(int eaterNetId, int jellyPrefabId)
        {
            NetIdentity eater = NetWorld.Instance != null ? NetWorld.Instance.Find(eaterNetId) : null;
            string who = eater != null ? ("P" + eater.OwnerId) : ("net" + eaterNetId);

            NetManager.Instance.AddLog(who + " 흡수! (젤리 " + jellyPrefabId + ")");
        }
    }
}
