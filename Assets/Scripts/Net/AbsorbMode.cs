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

        readonly NetWriter _w = new NetWriter();

        /// <summary>지금 살아있는 젤리들의 netId (스폰/디스폰 이벤트로 유지).</summary>
        readonly HashSet<int> _jellies = new HashSet<int>();

        /// <summary>
        /// 그중 호스트가 런타임에 뿌린 것만. maxJellyCount는 이것만 센다.
        ///
        /// ★ 맵에 미리 깔린 젤리(수백 개)까지 세면 상한을 즉시 넘겨
        ///   움직이는 젤리가 하나도 스폰되지 않는다. (실제로 겪음)
        /// </summary>
        readonly HashSet<int> _runtimeJellies = new HashSet<int>();

        /// <summary>이미 요청을 보낸 젤리 — 왕복 대기 중 중복 요청을 막는다.</summary>
        readonly HashSet<int> _requested = new HashSet<int>();

        float _spawnTimer;
        NetIdentity _myPlayer;

        /// <summary>호스트가 관리하는 부활 예약.</summary>
        struct PendingRespawn { public float At; public int NetId; }
        readonly List<PendingRespawn> _respawns = new List<PendingRespawn>();

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
            _runtimeJellies.Clear();
            _requested.Clear();
            _myPlayer = null;
            _spawnTimer = 0f;
        }

        // ─────────────────────────────────────────────
        //  젤리 목록 유지
        // ─────────────────────────────────────────────
        void HandleSpawned(NetIdentity id)
        {
            if (IsJelly(id))
            {
                _jellies.Add(id.NetId);
                if (id.NetId < NetConfig.SceneIdBase) _runtimeJellies.Add(id.NetId);
            }
            // ★ 봇을 빼야 한다.
            //   봇은 호스트 소유라 <b>호스트에서는 IsMine이 참</b>이다.
            //   이 조건이 없으면 봇이 스폰될 때마다 _myPlayer가 봇으로 덮어써져,
            //   흡수 판정이 내 캐릭터가 아니라 마지막에 태어난 봇의 위치·크기로
            //   돌아간다. 그래서 호스트에서 플레이어끼리 흡수가 통째로 죽었다.
            else if (id.IsMine && !id.IsBot) _myPlayer = id;      // 내 캐릭터 기억
        }

        void HandleDespawned(int netId)
        {
            _jellies.Remove(netId);
            _runtimeJellies.Remove(netId);
            _requested.Remove(netId);
            if (_myPlayer != null && _myPlayer.NetId == netId) _myPlayer = null;
        }

        static bool IsJelly(NetIdentity id)
        {
            // ★ 봇도 런타임 스폰 프리팹이라 PrefabId만 보면 젤리로 오인된다.
            if (id.IsBot) return false;
            return id.PrefabId >= NetConfig.JellyPrefabStart;
        }

        void Log(string msg)
        {
            if (verboseLog) Debug.Log("[흡수] " + msg);
        }

        // ─────────────────────────────────────────────
        void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            // 젤리는 대기 중에도 미리 깔아둔다(시작하면 바로 먹을 수 있게)
            if (net.IsHost && LanGameFlow.IsMode(GameModeType.Absorb))
            {
                HostSpawnTick();
                HostRespawnTick();
            }

            // ★ 먹기·흡수는 게임이 실제로 진행 중일 때만
            if (!LanGameFlow.IsPlaying(GameModeType.Absorb)) return;

            // 젤리 먹기는 거리로 검사하지 않는다.
            // 기존 경로(PlayerAbsorber 트리거 → JellyColliderAbsorb 흡수 연출 → OnAbsorbed)가
            // RequestEat을 불러준다. 여기서 또 검사하면 연출을 건너뛰고 이중 요청이 된다.
            if (useDistanceEating) CheckMyEating();

            CheckMyPlayerAbsorb();
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

            // ★ 맵에 미리 깔린 젤리는 상한에 안 넣는다. 런타임 스폰분만 센다.
            if (_runtimeJellies.Count >= maxJellyCount) return;

            GameObject[] prefabs = NetWorld.Instance.prefabs;
            if (prefabs == null || prefabs.Length <= NetConfig.JellyPrefabStart)
            {
                Debug.LogWarning("[AbsorbMode] 젤리 프리팹이 등록되지 않았습니다 (Prefabs 1번 이후).");
                spawnJelly = false;   // 로그 도배 방지
                return;
            }

            int prefabId = PickJellyPrefab(prefabs);
            if (prefabId < 0) return;

            Vector3 pos = PickJellySpawnPos();

            // ★ 젤리의 소유자를 호스트로 둔다.
            //   Wandering 젤리는 NavMeshAgent로 스스로 움직인다. 주인이 없으면 아무도
            //   위치를 보내지 않아 각 클라에서 제각각 돌아다닌다.
            //   호스트 소유로 하면 호스트에서만 AI가 돌고, 그 결과가 NetTransform으로 전파된다.
            NetIdentity spawned = NetWorld.Instance.HostSpawn(prefabId, NetHost.HostId, pos);

            // ★ NavMeshAgent 안착
            //
            //   Instantiate 시점에 위치가 NavMesh와 조금이라도 어긋나면 Unity가
            //   "Failed to create agent..." 경고와 함께 <b>agent를 꺼버린다.</b>
            //   그 상태로 Warp을 호출해도 enabled가 false라 아무 일도 안 일어나고,
            //   젤리는 바닥에 박힌 채 멈춰 있는다. (실제로 겪음)
            //
            //   그래서 껐다 → 위치 맞추고 → 다시 켜고 → Warp 순서로 안착시킨다.
            if (spawned != null)
            {
                UnityEngine.AI.NavMeshAgent ag =
                    spawned.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();

                if (ag != null)
                {
                    ag.enabled = false;
                    ag.transform.position = pos;
                    ag.enabled = true;

                    if (ag.isOnNavMesh) ag.Warp(pos);
                    else Log("젤리 net" + spawned.NetId + " 가 NavMesh에 못 올라감 — 위치 " + pos);
                }
            }
        }

        List<int> _jellyPrefabIds;

        /// <summary>
        /// 뿌릴 젤리 프리팹을 고른다.
        ///
        /// ★ 왜 Random.Range(1, Length)면 안 되는가
        ///   "1번 이후는 전부 젤리"라는 전제는 봇 프리팹이 배열에 들어오는 순간 깨진다.
        ///   그대로 두면 젤리 스폰 타이머가 <b>봇을 젤리로 뿌린다.</b>
        ///   그렇다고 젤리를 2번 이후로 미루면 씬에 깔린 젤리 208개의 PrefabId를
        ///   전부 다시 매겨야 한다. 그래서 인덱스 규칙 대신 프리팹 내용을 본다.
        ///
        ///   덕분에 봇을 배열 <b>맨 뒤에 그냥 추가</b>하면 되고, 나중에 다른 종류의
        ///   네트워크 오브젝트가 늘어도 여기는 손댈 필요가 없다.
        /// </summary>
        int PickJellyPrefab(GameObject[] prefabs)
        {
            if (_jellyPrefabIds == null)
            {
                _jellyPrefabIds = new List<int>();
                for (int i = NetConfig.JellyPrefabStart; i < prefabs.Length; i++)
                {
                    GameObject p = prefabs[i];
                    if (p == null) continue;
                    if (p.GetComponentInChildren<AIPlayerMovement>(true) != null) continue;  // 봇
                    if (p.GetComponentInChildren<PlayerMovement>(true) != null) continue;    // 플레이어
                    _jellyPrefabIds.Add(i);
                }

                if (_jellyPrefabIds.Count == 0)
                    Debug.LogWarning("[AbsorbMode] 젤리로 쓸 프리팹이 없습니다. "
                                     + "NetWorld.prefabs의 1번 이후를 확인해주세요.");
            }

            if (_jellyPrefabIds.Count == 0) return -1;
            return _jellyPrefabIds[Random.Range(0, _jellyPrefabIds.Count)];
        }

        Vector3[] _navVerts;

        /// <summary>
        /// 젤리를 뿌릴 위치. <b>반드시 NavMesh 위여야 한다.</b>
        ///
        /// ★ 왜
        ///   Wandering 젤리는 NavMeshAgent로 움직인다. NavMesh 밖에 놓으면
        ///   "Failed to create agent because it is not close enough to the NavMesh"가 뜨고
        ///   젤리가 그 자리에 멈춰 있는다.
        ///
        ///   맵 원점이 (0,0,0)이 아닐 수 있으므로(이 맵은 (110,-84,-280) 근처)
        ///   좌표 범위를 인스펙터로 넣는 방식은 쓸 수 없다. NavMesh에서 직접 뽑는다.
        /// </summary>
        Vector3 PickJellySpawnPos()
        {
            if (_navVerts == null)
            {
                UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
                _navVerts = tri.vertices;

                if (_navVerts == null || _navVerts.Length == 0)
                    Debug.LogWarning("[AbsorbMode] NavMesh가 없습니다 — 젤리를 원점 근처에 뿌립니다. "
                                     + "맵에 NavMesh를 구워야 움직이는 젤리가 제대로 배치됩니다.");
            }

            if (_navVerts != null && _navVerts.Length > 0)
            {
                Vector3 v = _navVerts[Random.Range(0, _navVerts.Length)];

                // 정점은 가장자리에 몰리므로 살짝 흩어 놓고 다시 NavMesh로 끌어당긴다
                v += new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));

                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(v, out hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                    return hit.position;   // 높이도 NavMesh를 그대로 따른다

                return v;
            }

            // 폴백 — NavMesh가 없을 때만
            return new Vector3(Random.Range(-spawnRangeX, spawnRangeX), spawnHeight,
                               Random.Range(-spawnRangeZ, spawnRangeZ));
        }

        // ═════════════════════════════════════════════
        //  전원: 내 캐릭터가 젤리에 닿았는지 검사 → 요청
        // ═════════════════════════════════════════════
        void CheckMyEating()
        {
            if (_myPlayer == null) { _myPlayer = FindMyPlayer(); if (_myPlayer == null) return; }
            if (NetWorld.Instance == null) return;

            float myR = playerRadius * ScaleOf(_myPlayer);
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
                if (!IsJelly(kv.Value) && !kv.Value.IsBot && kv.Value.IsMine) return kv.Value;
            return null;
        }

        /// <summary>"이 젤리 먹었다"고 호스트에 요청. 판정은 호스트가 한다.</summary>
        public void RequestEat(int jellyNetId, int eaterNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null) { Log("요청 불가 — NetManager 없음"); return; }

            Log("요청: 젤리 net" + jellyNetId + " ← 먹는이 net" + eaterNetId
                + " (내 모드 " + net.CurrentMode + ")");

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

            // ★ 호스트도 다시 검사한다 — 클라가 대기/종료 중에 요청을 보낼 수 있다
            if (!LanGameFlow.IsPlaying(GameModeType.Absorb))
            { Log("거부: 진행 중이 아님 (단계 " + (LanGameFlow.Instance != null ? LanGameFlow.Instance.Phase.ToString() : "?") + ")"); return; }

            NetIdentity jelly = NetWorld.Instance.Find(jellyNetId);
            if (jelly == null) { Log("탈락: 젤리 net" + jellyNetId + " 없음(이미 먹혔거나 네트워크 오브젝트가 아님)"); return; }
            if (!IsJelly(jelly)) { Log("거부: net" + jellyNetId + " 는 젤리가 아님 (prefabId " + jelly.PrefabId + ")"); return; }

            NetIdentity eater = NetWorld.Instance.Find(eaterNetId);
            if (eater == null) { Log("탈락: 먹는이 net" + eaterNetId + " 없음"); return; }

            // ★ 소유권 검사: 남의 캐릭터를 내세워 먹으려는 요청은 무시
            if (eater.OwnerId != requesterId)
            { Log("거부: 소유권 불일치 (요청자 P" + requesterId + " ≠ 소유자 P" + eater.OwnerId + ")"); return; }

            // 거리 검사: 너무 멀면 무시 (치트/지연 방어). 지연을 감안해 넉넉히 잡는다.
            NetScale es = eater.GetComponent<NetScale>();
            float eaterR = playerRadius * ScaleOf(eater);
            Vector3 gap = jelly.transform.position - eater.transform.position;
            gap.y = 0f;
            float allow = (eaterR + jellyRadius) * 2.5f;
            if (gap.sqrMagnitude > allow * allow)
            { Log("거부: 너무 멂 (거리 " + gap.magnitude.ToString("F2") + " > 허용 " + allow.ToString("F2") + ")"); return; }

            // 젤리의 '색 종류'를 읽어둔다 — 기존 PlayerColorVisual이 이걸로 색을 바꾼다
            JellyObject jo = jelly.GetComponent<JellyObject>();
            int colorType = jo != null ? (int)jo.jellyType : (int)JellyColorType.None;

            // ① 젤리를 먼저 없앤다 → 이 시점부터 후발 요청은 자동 탈락
            NetWorld.Instance.HostDespawn(jellyNetId);

            // ② 크기·색은 아래 EatJellyConfirm에서 PlayerAbsorber.AbsorbColor로 한 번에 처리한다.
            //    (AbsorbColor → OnJellyEaten/OnJellyScored → 기존 색·성장·점수 경로가 그대로 탄다)
            //    판정 거리 계산용 배율만 여기서 유지한다.
            if (es != null) es.HostGrow(growPerJelly);

            LanPlayerState eps = eater.GetComponent<LanPlayerState>();
            if (eps != null) eps.HostAddScore(scorePerJelly);

            // ③ 확정 통보 — 색 적용은 여기서
            _w.Begin(MsgType.EatJellyConfirm);
            _w.WriteInt(eaterNetId);
            _w.WriteInt(colorType);
            _w.End();
            net.Host.Broadcast(_w);

            OnEatConfirmed(eaterNetId, colorType);   // 호스트 자신도 반영
        }

        // ═════════════════════════════════════════════
        //  플레이어 ↔ 플레이어 흡수
        // ═════════════════════════════════════════════

        /// <summary>내 몸이 나보다 작은 상대에 닿았는지 검사 → 요청.</summary>
        void CheckMyPlayerAbsorb()
        {
            if (_myPlayer == null || NetWorld.Instance == null) return;
            if (IsOutOfPlay(_myPlayer)) return;               // 나부터 판 안에 있어야

            float myScale = ScaleOf(_myPlayer);
            Vector3 myPos = _myPlayer.transform.position;

            int target = -1;
            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity other = kv.Value;
                if (other == null || other == _myPlayer) continue;
                if (IsJelly(other)) continue;

                // ★ [LAN 이식] 봇은 전부 호스트 소유다.
                //   OwnerId만 비교하면 <b>호스트 플레이어가 봇을 영영 못 먹는다</b>
                //   (자기 편으로 취급되므로). ResolveAbsorbPlayer는 이미 고쳤는데
                //   요청을 만드는 이쪽이 남아 있어서, 호스트로 테스트하면
                //   흡수가 아예 시작되지 않았다.
                if (!other.IsBot && other.OwnerId == _myPlayer.OwnerId) continue;

                if (IsOutOfPlay(other)) continue;             // 이미 흡수/탈락한 상대

                float otherScale = ScaleOf(other);
                if (myScale < otherScale * absorbSizeRatio) continue;   // 내가 충분히 커야

                Vector3 d = other.transform.position - myPos;
                d.y = 0f;
                float touch = (myScale + otherScale) * playerRadius;
                if (d.sqrMagnitude > touch * touch) continue;

                target = other.NetId;
                break;
            }

            if (target >= 0) RequestAbsorbPlayer(target, _myPlayer.NetId);
        }

        void RequestAbsorbPlayer(int victimNetId, int absorberNetId)
        {
            NetManager net = NetManager.Instance;

            if (net.IsHost) { ResolveAbsorbPlayer(NetHost.HostId, victimNetId, absorberNetId); return; }

            _w.Begin(MsgType.AbsorbPlayerRequest);
            _w.WriteInt(victimNetId);
            _w.WriteInt(absorberNetId);
            _w.End();
            net.Client.Send(_w);
        }

        /// <summary>
        /// 호스트 판정. 원본 RPC_RequestAbsorbValidation의 규칙을 그대로 옮겼다.
        ///   ① 몸이 닿았나  ② 흡수자가 충분히 큰가  ③ 둘 다 판 안에 있나
        /// </summary>
        /// <summary>
        /// [LAN] 호스트가 굴리는 개체(AI 봇)가 흡수했을 때의 진입점.
        ///
        /// 봇은 호스트 소유라 요청/응답을 왕복할 필요가 없다. 검증은 그대로 받는다.
        /// </summary>
        public void HostAbsorb(int victimNetId, int absorberNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost) return;
            ResolveAbsorbPlayer(NetHost.HostId, victimNetId, absorberNetId);
        }

        void ResolveAbsorbPlayer(int requesterId, int victimNetId, int absorberNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null) return;
            if (!LanGameFlow.IsPlaying(GameModeType.Absorb)) return;

            NetIdentity victim = NetWorld.Instance.Find(victimNetId);
            NetIdentity absorber = NetWorld.Instance.Find(absorberNetId);
            if (victim == null || absorber == null) return;

            if (absorber.OwnerId != requesterId) return;          // 소유권

            // ★ [LAN 이식] '자기 편' 판정을 OwnerId가 아니라 개체로 본다.
            //   봇은 전부 호스트 소유라 예전 조건(OwnerId 비교)이면
            //   호스트 플레이어가 봇을 못 먹고, 봇끼리도 서로 못 먹는다.
            //   막고 싶었던 건 '자기 자신'이지 '같은 주인'이 아니었다.
            if (victim == absorber) return;
            if (!victim.IsBot && !absorber.IsBot && victim.OwnerId == absorber.OwnerId) return;

            if (IsJelly(victim) || IsJelly(absorber)) return;     // 젤리는 대상 아님
            if (IsOutOfPlay(victim) || IsOutOfPlay(absorber)) return;

            float vScale = ScaleOf(victim);
            float aScale = ScaleOf(absorber);

            if (aScale < vScale * absorbSizeRatio) return;        // 크기 조건

            // 몸이 닿는 거리인가 (지연 감안해 여유)
            Vector3 gap = victim.transform.position - absorber.transform.position;
            gap.y = 0f;
            float touch = (aScale + vScale) * playerRadius * 1.5f;
            if (gap.sqrMagnitude > touch * touch) return;

            // ── 확정 ──
            LanPlayerState vs = victim.GetComponent<LanPlayerState>();
            LanPlayerState asx = absorber.GetComponent<LanPlayerState>();
            NetScale vsc = victim.GetComponent<NetScale>();
            NetScale asc = absorber.GetComponent<NetScale>();

            // ★ 부활이 아니라 탈락이다(원본과 동일). Eliminated로 표시해야
            //   LanGameFlow의 생존자 집계에서 빠지고 게임이 끝날 수 있다.
            //   ★ Absorbed도 같이 켠다. 이게 없으면 LanPlayerState가 이 탈락을
            //     초콜릿 사고로 오인해 "초콜릿에 빠졌습니다" 창을 띄운다
            //     (흡수 연출 쪽에서 띄우는 창과 겹쳐 두 번 뜬다).
            if (vs != null)
            {
                vs.HostSetFlag(PlayerFlags.Absorbed, true);
                vs.HostSetFlag(PlayerFlags.Eliminated, true);
            }

            // 기존 GrowByAbsorbing(상대 크기)을 전원이 부르게 한다
            LanPlayerVisual avis = absorber.GetComponent<LanPlayerVisual>();
            float victimScaleValue = avis != null
                ? (victim.GetComponent<LanPlayerVisual>() != null
                    ? victim.GetComponent<LanPlayerVisual>().ScaleValue : vScale)
                : vScale;
            NetWorld.Instance.BroadcastGrow(absorberNetId, GrowKind.Absorbing, victimScaleValue);

            if (asc != null) asc.HostGrow(vScale * absorbGrowthRatio);
            if (asx != null) asx.HostAddScore(Mathf.RoundToInt(vScale * absorbScorePerScale));

            // 흡수당한 쪽은 크기를 원래대로
            if (vsc != null) { vsc.SetTarget(1f); NetWorld.Instance.BroadcastScale(victimNetId, 1f); }

            _w.Begin(MsgType.PlayerAbsorbed);
            _w.WriteInt(victimNetId);
            _w.WriteInt(absorberNetId);
            _w.End();
            net.Host.Broadcast(_w);

            OnPlayerAbsorbed(victimNetId, absorberNetId);

            // 부활 예약 — 기본은 끔(원본은 관전 전환). 켜면 respawnDelay 뒤 되살아난다.
            if (respawnAfterAbsorb)
            {
                PendingRespawn pr;
                pr.At = Time.time + respawnDelay;
                pr.NetId = victimNetId;
                _respawns.Add(pr);
            }
        }

        /// <summary>호스트: 예약된 부활을 처리한다.</summary>
        void HostRespawnTick()
        {
            if (_respawns.Count == 0) return;

            float now = Time.time;
            for (int i = _respawns.Count - 1; i >= 0; i--)
            {
                if (_respawns[i].At > now) continue;

                int netId = _respawns[i].NetId;
                _respawns.RemoveAt(i);

                NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
                if (id == null) continue;

                // 부활은 스폰포인트로 (없으면 기존 랜덤 범위)
                Vector3 pos = LanSpawnPoints.Instance != null
                    ? LanSpawnPoints.Instance.Random_()
                    : new Vector3(Random.Range(-spawnRangeX, spawnRangeX), spawnHeight,
                                  Random.Range(-spawnRangeZ, spawnRangeZ));

                LanPlayerState ps = id.GetComponent<LanPlayerState>();
                if (ps != null) ps.HostSetFlag(PlayerFlags.Absorbed, false);

                _w.Begin(MsgType.PlayerRespawn);
                _w.WriteInt(netId);
                _w.WriteFloat(pos.x); _w.WriteFloat(pos.y); _w.WriteFloat(pos.z);
                _w.End();
                NetManager.Instance.Host.Broadcast(_w);

                ApplyRespawn(netId, pos);
            }
        }

        /// <summary>
        /// 흡수 확정을 전원이 반영한다.
        ///
        /// ★ 원본은 부활이 아니라 <b>관전 전환</b>이다.
        ///   흡수당한 캐릭터는 빨려 들어가는 연출 뒤 SetActive(false)로 사라지고,
        ///   당한 본인에게만 게임오버 화면이 뜬다. (원본 AbsorbedSequence + GameOver)
        /// </summary>
        void OnPlayerAbsorbed(int victimNetId, int absorberNetId)
        {
            if (NetWorld.Instance == null) return;

            NetIdentity v = NetWorld.Instance.Find(victimNetId);
            NetIdentity a = NetWorld.Instance.Find(absorberNetId);

            NetManager.Instance.AddLog(
                "P" + (a != null ? a.OwnerId : 0) + " 가 P" + (v != null ? v.OwnerId : 0) + " 를 흡수!");

            if (v == null) return;

            Transform absorberTf = a != null ? a.transform : null;

            // ① 흡수 연출 + 사라짐 — 전원이 각자 재생하므로 모든 화면에서 없어진다
            //
            //   ★ 봇과 사람은 연출 후 처리가 다르다.
            //     사람  : 오브젝트를 남기고 비활성(관전으로 전환)
            //     봇    : 호스트가 아예 회수(Despawn) — 원본의 PhotonNetwork.Destroy와 동일
            if (v.IsBot)
            {
                AIPlayerMovement bot = v.GetComponent<AIPlayerMovement>();
                if (bot != null) bot.ApplyAbsorbedFromNet(absorberTf);
                else v.gameObject.SetActive(false);
                return;                                  // 봇은 게임오버 창이 없다
            }

            LanPlayerVisual vv = v.GetComponent<LanPlayerVisual>();
            if (vv != null) vv.PlayAbsorbed(absorberTf);
            else v.gameObject.SetActive(false);

            // ② 당한 사람 본인에게만 게임오버
            if (v.IsMine && LanGameFlow.Instance != null)
                LanGameFlow.Instance.ShowLocalGameOver("흡수당했습니다!\n관전 중...");
        }

        void ApplyRespawn(int netId, Vector3 pos)
        {
            NetIdentity id = NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
            if (id == null) return;

            // 위치는 소유자만 실제로 옮긴다(그 뒤 TransformUpdate로 전파됨)
            if (id.IsMine) id.transform.position = pos;

            NetScale ns = id.GetComponent<NetScale>();
            if (ns != null) ns.SetImmediate(1f);

            NetManager.Instance.AddLog("net" + id.NetId + " 부활");
        }

        static bool IsOutOfPlay(NetIdentity id)
        {
            // ★ 봇은 LanPlayerState가 없다(있으면 사람 목록에 섞인다).
            //   판 밖 여부는 AIPlayerMovement가 들고 있으므로 그쪽을 본다.
            //   이 갈래가 없으면 이미 흡수 연출 중인 봇을 다른 사람이 또 먹는다.
            if (id.IsBot)
            {
                AIPlayerMovement bot = id.GetComponent<AIPlayerMovement>();
                return bot != null && bot.IsOutOfPlay;
            }

            LanPlayerState ps = id.GetComponent<LanPlayerState>();
            return ps != null && ps.IsOutOfPlay;
        }

        /// <summary>
        /// 판정에 쓰는 '실제 크기'.
        ///
        /// ★ 실제 게임의 크기는 PlayerScaleController가 관리한다(시작 2.0, 범위 1~5).
        ///   NetScale.Current(배율 1.0 기준)를 보면 젤리를 아무리 먹어도 1.0이라
        ///   흡수 조건이 영원히 성립하지 않는다. 반드시 게임 쪽 값을 봐야 한다.
        /// </summary>
        public static float ScaleOf(NetIdentity id)
        {
            LanPlayerVisual v = id.GetComponent<LanPlayerVisual>();
            if (v != null && v.HasScaleController) return v.ScaleValue;

            NetScale s = id.GetComponent<NetScale>();
            return s != null ? s.Current : 1f;
        }

        // ═════════════════════════════════════════════
        //  클라이언트: 확정 결과 수신
        // ═════════════════════════════════════════════
        void HandleClientMessage(MsgType type, NetReader r)
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

        /// <summary>
        /// 흡수 확정 — 기존 보상 경로를 그대로 탄다.
        ///
        /// ★ PlayerAbsorber.AbsorbColor 한 번이면 색·성장·점수가 전부 처리된다.
        ///   AbsorbColor → OnJellyScored(점수) / OnJellyEaten(색 + 성장) 이벤트를 쏘고,
        ///   PlayerColorVisual·PlayerScaleController가 이미 그걸 구독하고 있다.
        ///   원본 Photon판의 RPC_ConfirmEat이 하던 일과 정확히 같다.
        ///
        /// ★ 원본과 다른 점: 소유자뿐 아니라 <b>전원</b>이 호출한다.
        ///   Photon판은 소유자만 적용하고 결과를 따로 동기화했지만,
        ///   AbsorbColor는 '같은 젤리 → 같은 결과'라 전원이 불러도 화면이 일치한다.
        ///   (컴포넌트가 비활성이어도 메서드 직접 호출은 동작한다)
        /// </summary>
        void OnEatConfirmed(int eaterNetId, int colorType)
        {
            NetIdentity eater = NetWorld.Instance != null ? NetWorld.Instance.Find(eaterNetId) : null;
            if (eater == null) return;

            PlayerAbsorber absorber = eater.GetComponentInChildren<PlayerAbsorber>(true);
            if (absorber != null) absorber.AbsorbColor((JellyColorType)colorType);
            else
            {
                // 폴백 — 테스트 씬처럼 PlayerAbsorber가 없는 캡슐용
                LanPlayerVisual vis = eater.GetComponent<LanPlayerVisual>();
                if (vis != null) vis.ApplyJellyColor((JellyColorType)colorType);
            }

            NetManager.Instance.AddLog("P" + eater.OwnerId + " 흡수! (" + (JellyColorType)colorType + ")");
        }
    }
}
