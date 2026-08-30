using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

        [Header("플레이어 흡수 판정")]
        [Tooltip("캡슐 반지름(스케일 1 기준). 플레이어끼리 닿았는지 재는 데 쓴다.")]
        public float playerRadius = 0.5f;

        [Tooltip("흡수 요청이 어디서 거부되는지 콘솔에 찍는다. 문제 해결 후 끌 것.")]
        public bool verboseLog = true;

        [Header("플레이어 흡수")]
        [Tooltip("상대를 흡수하려면 내가 이 배수보다 커야 한다. 1이면 조금만 커도 됨.")]
        public float absorbSizeRatio = 1.15f;

        private readonly NetWriter w = new NetWriter();

        // ★ 스포너가 뿌린 젤리만 센다
        //   maxJellyCount는 "소환기가 맵에 몇 개까지 유지할까"라는 뜻이다.
        //   씬에 손으로 배치한 사탕·젤리는 SCENE_ID_BASE(1,000,000) 이상의 netId를 받는데,
        //   그것까지 세면 씬 소품 300개만으로 상한을 넘어 스포너가 영영 아무것도 안 뿌린다.
        private readonly HashSet<int> runtimeJellies = new HashSet<int>();

        private float spawnTimer;
        private float scoreTimer;
        private NetIdentity myPlayer;

        //매 프레임 순회에 재사용한다. 새로 만들면 초당 60개의 쓰레기가 생긴다
        private readonly List<NetIdentity> characters = new List<NetIdentity>();

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
                HostScoreTick();
            }

            if (!IsPlaying)
                return;

            CheckMyPlayerAbsorb();
        }

        [Header("점수")]
        [Tooltip("크기에서 점수를 다시 뽑는 주기(초).")]
        public float scoreRecomputeInterval = 0.25f;

        // ═══════════════════════════════════════════════════════
        //  흡수 모드의 점수 규칙 — "점수는 지금 크기에서 나온다"
        // ═══════════════════════════════════════════════════════
        //
        // ★ 왜 모드가 들고 있어야 하나
        //   예전엔 이 규칙이 LanPlayerState(사람)와 LanBotState(봇) 안에
        //   `if (IsMode(Push)) return;` 가드를 달고 따로 살았다. 둘 다 두 모드에서
        //   쓰이는 공통 컴포넌트인데 흡수 전용 규칙을 품고 있었던 셈이고,
        //   그래서 주기(0.25초 vs 크기전송 주기)도 방송 여부도 서로 어긋났다.
        //   모드 전용 규칙은 모드가 알고, 적는 일은 NetEntity가 한다.
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
                Debug.LogWarning("[AbsorbMode] 젤리 프리팹이 등록되지 않았습니다 (Prefabs 1번 이후).");
                spawnJelly = false;
                return;
            }

            int prefabId = PickJellyPrefab(prefabs);
            if (prefabId < 0)
                return;

            //자리를 못 찾으면 이번 주기는 거른다. 예전엔 검증 안 된 좌표에 그냥 뿌리고
            //로그만 남겨서, 떠 있거나 못 움직이는 젤리가 맵에 쌓였다.
            //1.5초 뒤 다음 주기가 다시 시도하므로 빠뜨리는 비용이 거의 없다
            Vector3 pos;
            if (!TryPickJellySpawnPos(prefabs[prefabId], out pos))
            {
                Log("젤리 놓을 자리를 못 찾음 — 이번 주기 건너뜀");
                return;
            }

            NetIdentity spawned = NetWorld.Instance.HostSpawn(prefabId, NetHost.HOST_ID, pos);

            if (spawned == null)
                return;

            NavMeshAgent ag =
                spawned.GetComponentInChildren<NavMeshAgent>();

            if (ag == null)
                return;

            //NavMeshAgent는 '켜지는 순간의 자리'에서 NavMesh를 찾는다. 켜둔 채 옮기면 늦다
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

        //지터가 정점에서 최대 √(3²+3²) ≈ 4.24m 밀어내므로 그만큼만 되돌리면 된다.
        //예전엔 8m였는데, SamplePosition은 '길'이 아니라 '직선거리'로 찾기 때문에
        //반경이 넓을수록 얇은 벽 너머로 스냅될 여지가 커진다
        private const float SPAWN_SAMPLE_RADIUS = 5f;

        /// <summary>
        /// 젤리 하나를 놓을 자리를 고른다. 실패하면 false — 이번 주기는 거른다.
        ///
        /// ★ 왜 프리팹을 받는가 (에이전트 타입)
        ///   이 프로젝트엔 NavMesh가 둘이다.
        ///     PlayerJelly (타입 0)          radius 0.77  climb 1.0   ← 사람·봇
        ///     BearJelly   (타입 -334000983) radius 1.0   climb 0.6   ← 젤리
        ///   젤리가 더 뚱뚱하고 덜 오르므로 걸어다닐 수 있는 영역이 더 좁다.
        ///   예전엔 int 마스크 오버로드(NavMesh.AllAreas)를 썼는데 그건 타입 0 기준이라,
        ///   사람은 되고 젤리는 안 되는 자리가 그대로 통과해 "NavMesh에 못 올라감"이 났다.
        ///   프리팹의 agentTypeID로 필터를 만들어 그 젤리 기준으로 판정한다.
        /// </summary>
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

            //NavMeshAgent는 baseOffset만큼 떠 있는 걸 전제로 자리를 잡는다
            //표면 좌표를 그대로 주면 발밑이 NavMesh 아래로 내려가 "not close enough" 로 붙지 못한다
            if (agent != null)
                pos += Vector3.up * agent.baseOffset;

            return true;
        }

        private bool TrySampleNavMeshPos(NavMeshQueryFilter filter, out Vector3 pos)
        {
            pos = Vector3.zero;

            if (navVerts == null)
            {
                //인자가 없어서 두 NavMesh의 정점이 섞여 나온다. 남의 타입 정점은
                //아래 SamplePosition이 걸러내므로 씨앗으로만 쓴다
                NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
                navVerts = tri.vertices;

                if (navVerts == null || navVerts.Length == 0)
                    Debug.LogWarning("[AbsorbMode] NavMesh가 없습니다 — 젤리를 뿌릴 수 없습니다. "
                                     + "맵에 NavMesh를 구워야 합니다.");
            }

            if (navVerts == null || navVerts.Length == 0)
                return false;

            //navVerts는 최초 1회 캐시라 무너진 발판 위 좌표가 섞여 있다.
            //실패한 후보를 버리고 다시 뽑으므로 결과는 '살아남은 발판 위 균등'에 수렴한다
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
            {
                Log("요청 불가 — NetManager 없음");
                return;
            }

            Log("요청: 젤리 net" + jellyNetId + " ← 먹는이 net" + eaterNetId
                + " (내 모드 " + net.CurrentMode + ")");

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

        protected override void RegisterRoutes()
        {
            Net.RouteHost(MsgType.EatJellyRequest, (from, r) =>
            {
                int jellyNetId = r.ReadInt();
                int eaterNetId = r.ReadInt();
                ResolveEat(from.Id, jellyNetId, eaterNetId);
            });

            Net.RouteHost(MsgType.AbsorbPlayerRequest, (from, r) =>
            {
                int victimNetId = r.ReadInt();
                int absorberNetId = r.ReadInt();
                ResolveAbsorbPlayer(from.Id, victimNetId, absorberNetId);
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
            {
                Log("거부: 진행 중이 아님 (단계 " + (LanGameFlow.Instance != null ? LanGameFlow.Instance.Phase.ToString() : "?") + ")");
                return;
            }

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

            // ★ 거리 검사는 두지 않는다
            //   젤리는 호스트 소유인데 흡수 연출은 먹는 클라에서만 돈다.
            //   원격 아바타는 PlayerAbsorber가 꺼져 있어 호스트에선 OnTriggerEnter가
            //   안 터지고, 그래서 호스트의 젤리는 제자리에 그대로 있다.
            //   클라 화면에서 젤리가 몸속으로 빨려 들어가는 그 순간, 호스트가 재는 거리는
            //   '젤리 원래 자리 ↔ 연출이 끝난 뒤의 플레이어'다 — 0이 아니다.
            //
            //   예전엔 그 격차를 (반지름×2.5 + 5m)로 근사했다. 지금 수치로는 여유가
            //   넉넉해 정상 흡수가 걸리진 않았지만, 애초에 호스트가 볼 수 없는 사건을
            //   근사치로 재는 구조다 — 이동속도·감지 크기·연출 시간·InterpDelay 중
            //   무엇 하나만 조정해도 조용히 정상 흡수를 거부하기 시작한다.
            //   조율해야 유지되는 검사보다 없는 편이 낫다고 판단했다.
            //
            //   남은 방어: 소유권(위 OwnerId 검사) · 젤리 여부(IsJelly) ·
            //   선착순(이미 먹힌 젤리는 Find가 null) — 이중 흡수는 여전히 막힌다.

            JellyObject jo = jelly.GetComponent<JellyObject>();
            int colorType = jo != null ? (int)jo.JellyType : (int)JellyColorType.None;

            NetWorld.Instance.HostDespawn(jellyNetId);

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

        /// <summary>
        /// 내 캐릭터가 남을 흡수했다고 호스트에 알린다. 호출자는 CheckMyPlayerAbsorb 하나뿐이라
        /// absorberNetId는 언제나 내 캐릭터다 — 그래서 아래 requesterId가 항상 맞는다.
        /// </summary>
        private void RequestAbsorbPlayer(int victimNetId, int absorberNetId)
        {
            NetManager net = NetManager.Instance;

            if (net.IsHost)
            {
                //내가 호스트면 내 캐릭터의 OwnerId가 곧 HOST_ID다
                ResolveAbsorbPlayer(NetHost.HOST_ID, victimNetId, absorberNetId);
                return;
            }

            w.Begin(MsgType.AbsorbPlayerRequest);
            w.WriteInt(victimNetId);
            w.WriteInt(absorberNetId);
            w.End();
            net.Client.Send(w);
        }

        /// <summary>
        /// 봇이 남을 흡수했을 때. PushMode.HostBotBatHit과 같은 자리다.
        ///
        /// ★ 봇 전용이다 — 사람은 RequestAbsorbPlayer로 가야 한다
        ///   여기서 requesterId로 HOST_ID를 넘기는데, HostJudgement가
        ///   actor.OwnerId != requesterId 면 거절한다. 봇은 전부 호스트 소유라
        ///   성립하지만, 클라의 사람을 태우면 조용히 거절돼 흡수가 안 먹는 것처럼 보인다.
        /// </summary>
        public void HostBotAbsorb(int victimNetId, int botNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;

            //전제가 깨지면 HostJudgement가 조용히 거절해 흡수가 안 먹는 것처럼 보인다.
            //그때 어디를 봐야 하는지 알려준다
            NetIdentity bot = NetWorld.Instance != null
                ? NetWorld.Instance.Find(botNetId) : null;

            if (bot != null && bot.OwnerId != NetHost.HOST_ID)
            {
                Debug.LogWarning("[흡수] HostBotAbsorb는 호스트 소유(봇)만 쓸 수 있습니다. "
                    + "net" + botNetId + " 의 소유자는 P" + bot.OwnerId
                    + " 입니다 — RequestAbsorbPlayer를 쓰세요.");
                return;
            }

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

            //몸이 닿는 거리인가. 지연을 감안해 여유를 둔다
            if (!judgement.WithinReach((aScale + vScale) * playerRadius * 1.5f))
                return;

            LanPlayerState victimState = victim.PlayerState;

            if (victimState != null)
            {
                victimState.HostSetFlag(PlayerFlags.Absorbed, true);
                victimState.HostSetFlag(PlayerFlags.Eliminated, true);
            }

            //vScale이 이미 NetEntity.ScaleOf(victim) = LanPlayerVisual.ScaleValue 다.
            //예전엔 여기서 victim의 LanPlayerVisual을 두 번 더 찾아 같은 값을 다시 만들었고,
            //흡수자(avis)의 유무로 피해자의 크기를 고르는 관계없는 분기까지 끼어 있었다
            NetWorld.Instance.BroadcastGrow(absorberNetId, GrowKind.Absorbing, vScale);

            w.Begin(MsgType.PlayerAbsorbed);
            w.WriteInt(victimNetId);
            w.WriteInt(absorberNetId);
            w.End();
            NetManager.Instance.Host.Broadcast(w);

            OnPlayerAbsorbed(victimNetId, absorberNetId);
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

            //사람이든 봇이든 같은 연출이다. 무엇을 멈추고 끝나고 무엇을 할지는
            //LanPlayerVisual이 NetIdentity 캐시를 보고 갈라준다
            LanPlayerVisual victimVisual = v.Visual;
            if (victimVisual != null)
                victimVisual.PlayAbsorbed(absorberTf);
            else
                v.gameObject.SetActive(false);

            // ★ IsMine만으로는 "내가 조종하는 캐릭터"가 아니다
            //   봇은 전부 호스트 소유(OwnerId = NetHost.HOST_ID)라, 호스트 화면에서는
            //   맵의 모든 봇이 IsMine == true 다. 그래서 내가 봇을 흡수했을 뿐인데
            //   호스트에게 "흡수당했습니다! 관전 중..."이 뜨고 조작까지 잠겼다.
            //   (봇끼리 흡수하거나 클라가 봇을 먹어도 호스트에서 똑같이 터졌다)
            //   PushMode.SendKilledBy가 IsBot을 먼저 걸러내는 것과 같은 이유다.
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

            NetManager.Instance.AddLog("P" + eater.OwnerId + " 흡수! (" + (JellyColorType)colorType + ")");
        }
    }
}
