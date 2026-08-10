using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
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

        [Header("수치 출처")]
        [Tooltip("켜면 DataManager의 배트 수치를 쓴다. 끄면 위 인스펙터 값을 쓴다(테스트용).")]
        public bool useDataManagerValues = true;

        float BatRange
        {
            get
            {
                var dm = DataManager.Instance;
                return (useDataManagerValues && dm != null) ? dm.batRange : batRange;
            }
        }

        float BatPushForce
        {
            get
            {
                var dm = DataManager.Instance;
                return (useDataManagerValues && dm != null) ? dm.batPushForce : batPushForce;
            }
        }

        float BatCooldown
        {
            get
            {
                var dm = DataManager.Instance;
                return (useDataManagerValues && dm != null) ? dm.batCooldown : batCooldown;
            }
        }

        float BatHitGrowth
        {
            get
            {
                var dm = DataManager.Instance;
                return (useDataManagerValues && dm != null) ? dm.batHitGrowth : batHitGrowth;
            }
        }

        [Header("점수")]
        [Tooltip("밀치기에 성공할 때마다 얻는 점수.")]
        public int pushHitScore = 100;

        [Tooltip("내가 민 상대가 이 시간 안에 떨어지면 '내가 떨어뜨린 것'으로 본다(초).")]
        public float killCreditWindow = 5f;

        private readonly NetWriter w = new NetWriter();

        private readonly Dictionary<int, float> lastHitTime = new Dictionary<int, float>();

        private float localCooldown;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
            {
                Debug.LogError("[PushMode] NetManager가 없습니다.");
                return;
            }

            net.OnHostMessage += HandleHostMessage;
            net.OnClientMessage += HandleClientMessage;
            net.OnDisconnected += ResetAll;
        }

        private void OnDestroy()
        {
            NetManager net = NetManager.Instance;
            if (net == null)
                return;
            net.OnHostMessage -= HandleHostMessage;
            net.OnClientMessage -= HandleClientMessage;
            net.OnDisconnected -= ResetAll;
        }

        private void ResetAll()
        {
            lastHitTime.Clear();
            localCooldown = 0f;
        }

        private void Update()
        {
            NetManager net = NetManager.Instance;
            if (net == null || net.CurrentMode == NetManager.Mode.None)
                return;

            if (localCooldown > 0f)
                localCooldown -= Time.deltaTime;

            if (!LanGameFlow.IsPlaying(GameModeType.Push))
                return;

            if (PlayerMovement.Local != null)
                return;

            if (!Input.GetKeyDown(attackKey))
                return;
            if (localCooldown > 0f)
                return;

            NetIdentity me = NetEntity.FindMyPlayer();
            if (me == null)
                return;

            NetIdentity victim = FindNearestVictim(me);
            if (victim == null)
                return;

            localCooldown = BatCooldown;
            RequestBatHit(victim.NetId, me.NetId);
        }

        private NetIdentity FindNearestVictim(NetIdentity me)
        {
            if (NetWorld.Instance == null)
                return null;

            float myScale = NetEntity.ScaleOf(me);
            float reach = BatRange * myScale + 0.5f;
            float best = reach * reach;
            NetIdentity found = null;

            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity other = kv.Value;

                if (other == null || NetEntity.IsSameSide(me, other))
                    continue;

                if (NetEntity.IsJelly(other) || NetEntity.IsOutOfPlay(other))
                    continue;

                Vector3 d = other.transform.position - me.transform.position;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < best)
                {
                    best = sq;
                    found = other;
                }
            }
            return found;
        }

        public void HostBatHit(int victimNetId, int attackerNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost)
                return;
            ResolveBatHit(NetHost.HOST_ID, victimNetId, attackerNetId);
        }

        public void RequestBatHitPublic(int victimNetId, int attackerNetId)
        {
            RequestBatHit(victimNetId, attackerNetId);
        }

        private void RequestBatHit(int victimNetId, int attackerNetId)
        {
            NetManager net = NetManager.Instance;

            if (net.IsHost)
            {
                ResolveBatHit(NetHost.HOST_ID, victimNetId, attackerNetId);
                return;
            }

            w.Begin(MsgType.BatHitRequest);
            w.WriteInt(victimNetId);
            w.WriteInt(attackerNetId);
            w.End();
            net.Client.Send(w);
        }

        private void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
            if (type != MsgType.BatHitRequest)
                return;

            int victimNetId = r.ReadInt();
            int attackerNetId = r.ReadInt();
            ResolveBatHit(from.Id, victimNetId, attackerNetId);
        }

        private void ResolveBatHit(int requesterId, int victimNetId, int attackerNetId)
        {
            HostVerdict verdict = HostVerdict.Judge(GameModeType.Push, requesterId, attackerNetId, victimNetId);

            if (!verdict.Valid)
                return;

            NetIdentity attacker = verdict.Actor;
            NetIdentity victim = verdict.Target;

            if (NetEntity.IsJelly(victim))
                return;

            //쿨다운 키는 요청자가 아니라 공격자다. 봇은 전부 호스트 소유라 한 칸을 공유하게 된다
            float now = Time.time;

            if (lastHitTime.TryGetValue(attackerNetId, out float last) && now - last < BatCooldown)
                return;

            float aScale = NetEntity.ScaleOf(attacker);
            float vScale = NetEntity.ScaleOf(victim);

            if (!verdict.WithinReach((BatRange * aScale + vScale) * rangeTolerance))
                return;

            lastHitTime[attackerNetId] = now;

            float startScale = DataManager.Instance != null ? DataManager.Instance.startingScale : 1f;
            float force = BatPushForce * (aScale / Mathf.Max(0.01f, startScale));

            SendKnockback(victim, verdict.DirectionToTarget(), force);

            float growth = BatHitGrowth / Mathf.Max(aScale, 1f);

            NetWorld.Instance.BroadcastGrow(attackerNetId, GrowKind.BatHit, growth);

            NetScale attackerScale = attacker.GetComponent<NetScale>();
            if (attackerScale != null)
                attackerScale.HostGrow(growth);

            NetEntity.AddScore(attacker, pushHitScore);

            lastPusher[victimNetId] = new Credit { AttackerNetId = attackerNetId, At = now };

            NetManager.Instance.AddLog($"P{attacker.OwnerId} → P{victim.OwnerId} 배트 히트! (+{pushHitScore})");
        }

        struct Credit { public int AttackerNetId; public float At; }
        private readonly Dictionary<int, Credit> lastPusher = new Dictionary<int, Credit>();



        public void HostReportEliminated(int victimNetId)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost || NetWorld.Instance == null)
                return;
            if (!LanGameFlow.IsMode(GameModeType.Push))
                return;

            Credit c;
            if (!lastPusher.TryGetValue(victimNetId, out c))
                return;
            lastPusher.Remove(victimNetId);

            if (Time.time - c.At > killCreditWindow)
                return;

            NetIdentity victim = NetWorld.Instance.Find(victimNetId);
            NetIdentity attacker = NetWorld.Instance.Find(c.AttackerNetId);
            if (victim == null || attacker == null)
                return;

            int stolen = NetEntity.ScoreOf(victim);
            if (stolen <= 0)
                return;

            NetEntity.AddScore(attacker, stolen);
            net.AddLog("P" + attacker.OwnerId + " 가 떨어뜨림 — 점수 " + stolen + " 획득");
        }


        private void SendKnockback(NetIdentity victim, Vector3 dir, float force)
        {
            NetManager net = NetManager.Instance;

            if (victim.OwnerId == NetHost.HOST_ID)
            {
                ApplyKnockbackLocal(victim.NetId, dir, force);
                return;
            }

            NetHost.Peer target = net.Host.FindPeer(victim.OwnerId);
            if (target == null)
                return;

            w.Begin(MsgType.Knockback);
            w.WriteInt(victim.NetId);
            w.WriteFloat(dir.x);
            w.WriteFloat(dir.z);
            w.WriteFloat(force);
            w.End();
            net.Host.SendTo(target, w);
        }

        private void HandleClientMessage(MsgType type, NetReader r)
        {
            if (type != MsgType.Knockback)
                return;

            int victimNetId = r.ReadInt();
            float dx = r.ReadFloat();
            float dz = r.ReadFloat();
            float force = r.ReadFloat();

            ApplyKnockbackLocal(victimNetId, new Vector3(dx, 0f, dz), force);
        }

        private void ApplyKnockbackLocal(int victimNetId, Vector3 dir, float force)
        {
            NetIdentity victim = NetWorld.Instance != null ? NetWorld.Instance.Find(victimNetId) : null;
            if (victim == null)
                return;

            if (!victim.IsMine)
                return;

            PlayerMovement pm = victim.GetComponentInChildren<PlayerMovement>(true);
            if (pm != null && pm.enabled)
            {
                pm.ApplyKnockback(dir, force);
            }
            else
            {
                AIPlayerMovement bot = victim.GetComponent<AIPlayerMovement>();
                if (bot != null)
                {
                    bot.RPC_ApplyKnockback(dir.x, dir.z, force);
                }
                else
                {
                    NetKnockback kb = victim.GetComponent<NetKnockback>();
                    if (kb != null)
                        kb.Apply(dir, force);
                }
            }

            NetManager.Instance.AddLog("밀려남! (힘 " + force.ToString("F1") + ")");
        }


    }
}
