using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 밀치기 모드 규칙. Photon판 NetworkPlayerSync의 배트 판정부를 대신한다.
    ///
    /// 흐름은 흡수(AbsorbMode)와 완전히 같은 '요청 → 판정 → 결과' 패턴이다.
    ///   클라   : 스페이스로 배트 휘두름 → "이놈 때렸다" 요청
    ///   호스트 : 쿨다운·거리·소유권 검증 → 넉백 지시 + 공격자 성장
    ///
    /// ★ 흡수와 다른 점 하나
    ///   흡수 결과(젤리 소멸·크기)는 전원에게 방송했지만,
    ///   넉백은 <b>피격자 소유자에게만</b> 보낸다. 이유는 NetKnockback 주석 참고.
    /// </summary>
    public class PushMode : MonoBehaviour
    {
        public static PushMode Instance { get; private set; }

        [Header("배트")]
        public KeyCode attackKey = KeyCode.Space;
        [Tooltip("사거리 (내 크기에 비례해 늘어난다)")]
        public float batRange = 1.6f;
        [Tooltip("미는 힘 (내 크기에 비례해 세진다)")]
        public float batPushForce = 8f;
        [Tooltip("휘두르기 쿨다운(초). 호스트가 강제한다.")]
        public float batCooldown = 0.5f;
        [Tooltip("때리면 커지는 배율")]
        public float batHitGrowth = 0.06f;

        [Header("검증 여유")]
        [Tooltip("호스트 재검증 시 사거리에 곱하는 여유. 지연을 감안해 넉넉히.")]
        public float rangeTolerance = 1.6f;

        readonly NetWriter _w = new NetWriter();

        /// <summary>공격자 번호 → 마지막으로 인정된 히트 시각(호스트만 사용).</summary>
        readonly Dictionary<int, float> _lastHitTime = new Dictionary<int, float>();

        float _localCooldown;   // 내 화면에서 연타를 막는 용도(호스트 판정과 별개)

        // ─────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null) { Debug.LogError("[PushMode] NetManager가 없습니다."); return; }

            net.OnHostMessage += HandleHostMessage;
            net.OnClientMessage += HandleClientMessage;
            net.OnDisconnected += ResetAll;
        }

        void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net == null) return;
            net.OnHostMessage -= HandleHostMessage;
            net.OnClientMessage -= HandleClientMessage;
            net.OnDisconnected -= ResetAll;
        }

        void ResetAll()
        {
            _lastHitTime.Clear();
            _localCooldown = 0f;
        }

        // ═════════════════════════════════════════════
        //  전원: 공격 입력 → 요청
        // ═════════════════════════════════════════════
        void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None) return;

            if (_localCooldown > 0f) _localCooldown -= Time.deltaTime;

            if (!Input.GetKeyDown(attackKey)) return;
            if (_localCooldown > 0f) return;

            NetIdentity me = FindMyPlayer();
            if (me == null) return;

            NetIdentity victim = FindNearestVictim(me);
            if (victim == null) return;

            _localCooldown = batCooldown;
            RequestBatHit(victim.NetId, me.NetId);
        }

        /// <summary>사거리 안에서 가장 가까운 남의 플레이어를 고른다.</summary>
        NetIdentity FindNearestVictim(NetIdentity me)
        {
            if (NetWorld.Instance == null) return null;

            float myScale = ScaleOf(me);
            float reach = batRange * myScale + 0.5f;
            float best = reach * reach;
            NetIdentity found = null;

            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity other = kv.Value;
                if (other == null || other == me) continue;
                if (other.PrefabId >= NetConfig.JellyPrefabStart) continue;   // 젤리는 대상 아님
                if (other.OwnerId == me.OwnerId) continue;                    // 내 것끼리는 제외

                Vector3 d = other.transform.position - me.transform.position;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < best) { best = sq; found = other; }
            }
            return found;
        }

        void RequestBatHit(int victimNetId, int attackerNetId)
        {
            NetManager net = NetManager.Instance;

            if (net.IsHost)
            {
                ResolveBatHit(NetHost.HostId, victimNetId, attackerNetId);
                return;
            }

            _w.Begin(MsgType.BatHitRequest);
            _w.WriteInt(victimNetId);
            _w.WriteInt(attackerNetId);
            _w.End();
            net.Client.Send(_w);
        }

        // ═════════════════════════════════════════════
        //  호스트: 히트 판정 (권위)
        // ═════════════════════════════════════════════
        void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type != MsgType.BatHitRequest) return;

            int victimNetId = r.ReadInt();
            int attackerNetId = r.ReadInt();
            ResolveBatHit(from.Id, victimNetId, attackerNetId);
        }

        /// <summary>
        /// 공격자의 "때렸다"는 주장을 호스트가 다시 검증한다.
        ///
        /// 이 검증이 없으면 조작된 클라가 맵 반대편 상대를 아무 때나 밀어내고
        /// 성장 보상까지 공짜로 챙길 수 있다. (Photon판의 U2/N4·CBT-1 가드와 같은 이유)
        /// </summary>
        void ResolveBatHit(int requesterId, int victimNetId, int attackerNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null) return;

            NetIdentity attacker = NetWorld.Instance.Find(attackerNetId);
            NetIdentity victim = NetWorld.Instance.Find(victimNetId);
            if (attacker == null || victim == null) return;

            // ① 소유권 — 남의 캐릭터를 내세운 요청 차단
            if (attacker.OwnerId != requesterId) return;

            // ② 자기 자신·같은 소유자는 때릴 수 없다
            if (victim.OwnerId == attacker.OwnerId) return;

            // ③ 젤리를 때리려는 요청 차단
            if (victim.PrefabId >= NetConfig.JellyPrefabStart) return;

            // ④ 쿨다운 — 연사 스크립트로 무한 성장하는 것을 호스트가 막는다
            float now = Time.time;
            float last;
            if (_lastHitTime.TryGetValue(requesterId, out last) && now - last < batCooldown) return;

            // ⑤ 거리 재검증 — 클라의 판정을 믿지 않는다
            float aScale = ScaleOf(attacker);
            float vScale = ScaleOf(victim);
            float allow = (batRange * aScale + vScale) * rangeTolerance;

            Vector3 gap = victim.transform.position - attacker.transform.position;
            gap.y = 0f;
            if (gap.sqrMagnitude > allow * allow) return;

            _lastHitTime[requesterId] = now;

            // ⑥ 방향·힘 계산 (권위값)
            Vector3 dir = gap;
            if (dir.sqrMagnitude < 0.01f) dir = attacker.transform.forward;
            dir.y = 0f;
            dir.Normalize();

            float force = batPushForce * aScale;

            // ⑦ 넉백은 피격자 소유자에게만
            SendKnockback(victim, dir, force);

            // ⑧ 공격자 보상 — 크기가 클수록 이득이 줄게(역수) 해서 눈덩이를 완화
            NetScale asc = attacker.GetComponent<NetScale>();
            if (asc != null) asc.HostGrow(batHitGrowth / Mathf.Max(aScale, 1f));

            net.AddLog("P" + attacker.OwnerId + " → P" + victim.OwnerId + " 배트 히트!");
        }

        /// <summary>넉백 지시를 피격자 소유자 한 명에게만 보낸다.</summary>
        void SendKnockback(NetIdentity victim, Vector3 dir, float force)
        {
            NetManager net = NetManager.Instance;

            // 피격자가 호스트 자신이면 소켓을 거치지 않고 바로 적용
            if (victim.OwnerId == NetHost.HostId)
            {
                ApplyKnockbackLocal(victim.NetId, dir, force);
                return;
            }

            NetHost.Peer target = net.Host.FindPeer(victim.OwnerId);
            if (target == null) return;   // 이미 나감

            _w.Begin(MsgType.Knockback);
            _w.WriteInt(victim.NetId);
            _w.WriteFloat(dir.x);
            _w.WriteFloat(dir.z);
            _w.WriteFloat(force);
            _w.End();
            net.Host.SendTo(target, _w);
        }

        // ═════════════════════════════════════════════
        //  피격자: 넉백 수신
        // ═════════════════════════════════════════════
        void HandleClientMessage(MsgType type, NetReader r)
        {
            if (type != MsgType.Knockback) return;

            int victimNetId = r.ReadInt();
            float dx = r.ReadFloat();
            float dz = r.ReadFloat();
            float force = r.ReadFloat();

            ApplyKnockbackLocal(victimNetId, new Vector3(dx, 0f, dz), force);
        }

        void ApplyKnockbackLocal(int victimNetId, Vector3 dir, float force)
        {
            NetIdentity victim = NetWorld.Instance != null ? NetWorld.Instance.Find(victimNetId) : null;
            if (victim == null) return;

            // 내 캐릭터가 아니면 무시 — 남의 것은 위치 동기화로 따라가야 한다
            if (!victim.IsMine) return;

            NetKnockback kb = victim.GetComponent<NetKnockback>();
            if (kb != null) kb.Apply(dir, force);

            NetManager.Instance.AddLog("밀려남! (힘 " + force.ToString("F1") + ")");
        }

        // ─────────────────────────────────────────────
        static float ScaleOf(NetIdentity id)
        {
            NetScale s = id.GetComponent<NetScale>();
            return s != null ? s.Current : 1f;
        }

        NetIdentity FindMyPlayer()
        {
            if (NetWorld.Instance == null) return null;
            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity id = kv.Value;
                if (id != null && id.PrefabId < NetConfig.JellyPrefabStart && id.IsMine) return id;
            }
            return null;
        }
    }
}
