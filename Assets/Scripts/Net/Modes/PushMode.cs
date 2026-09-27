using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class PushMode : NetGameMode<PushMode>
    {
        public float rangeTolerance = 1.6f;

        public int pushHitScore = 100;

        public float killAssistSeconds = 5f;

        private readonly NetWriter w = new NetWriter();

        private readonly Dictionary<int, Credit> lastPusher = new Dictionary<int, Credit>();

        protected override GameModeType Mode
        {
            get { return GameModeType.Push; }
        }

        protected override void ResetAll()
        {
            lastPusher.Clear();
        }

        public void HostBotBatHit(int victimNetId, int botNetId)
        {
            if (!IsHost)
                return;

            ResolveBatHit(NetHost.HOST_ID, victimNetId, botNetId);
        }

        public void RequestBatHit(int victimNetId, int attackerNetId)
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
            net.SendToHost(w);
        }

        protected override void RegisterRoutes()
        {
            Net.RouteHost(MsgType.BatHitRequest, (from, r) =>
            {
                int victimNetId = r.ReadInt();
                int attackerNetId = r.ReadInt();
                ResolveBatHit(from, victimNetId, attackerNetId);
            });

            Net.RouteClient(MsgType.KilledBy, r => LanSpectator.ReportKiller(r.ReadInt()));

            Net.RouteClient(MsgType.Knockback, r =>
            {
                int victimNetId = r.ReadInt();
                float dx = r.ReadFloat();
                float dz = r.ReadFloat();
                float force = r.ReadFloat();

                ApplyKnockbackLocal(victimNetId, new Vector3(dx, 0f, dz), force);
            });
        }

        protected override void UnregisterRoutes()
        {
            Net.UnrouteHost(MsgType.BatHitRequest);
            Net.UnrouteClient(MsgType.KilledBy);
            Net.UnrouteClient(MsgType.Knockback);
        }

        private void ResolveBatHit(int requesterId, int victimNetId, int attackerNetId)
        {
            HostJudgement judgement = HostJudgement.Judge(Mode, requesterId, attackerNetId, victimNetId);

            if (!judgement.Valid)
                return;

            NetIdentity attacker = judgement.Actor;
            NetIdentity victim = judgement.Target;

            if (NetEntity.IsJelly(victim))
                return;

            DataManager rules = DataManager.Instance;

            if (rules == null)
                return;

            float aScale = NetEntity.ScaleOf(attacker);
            float vScale = NetEntity.ScaleOf(victim);

            if (!judgement.WithinReach((rules.BatRange * aScale + vScale) * rangeTolerance))
                return;

            float force = rules.BatPushForce * (aScale / Mathf.Max(0.01f, NetEntity.BaselineScale));

            SendKnockback(victim, judgement.DirectionToTarget(), force);

            float growth = rules.BatHitGrowth / Mathf.Max(aScale, 1f);

            NetWorld.Instance.BroadcastGrow(attackerNetId, GrowKind.BatHit, growth);

            NetEntity.AddScore(attacker, pushHitScore);

            lastPusher[victimNetId] = new Credit { AttackerNetId = attackerNetId, At = Time.time };
        }

        struct Credit
        {
            public int AttackerNetId;
            public float At;
        }

        public void HostAwardKillCredit(int victimNetId)
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

            if (Time.time - c.At > killAssistSeconds)
                return;

            NetIdentity victim = NetWorld.Instance.Find(victimNetId);
            NetIdentity attacker = NetWorld.Instance.Find(c.AttackerNetId);
            if (victim == null || attacker == null)
                return;

            SendKilledBy(victim, c.AttackerNetId);

            int stolen = NetEntity.ScoreOf(victim);
            if (stolen <= 0)
                return;

            NetEntity.AddScore(attacker, stolen);
        }

        private void SendKilledBy(NetIdentity victim, int killerNetId)
        {
            if (victim.IsBot)
                return;

            if (victim.IsMine)
            {
                LanSpectator.ReportKiller(killerNetId);
                return;
            }

            w.Begin(MsgType.KilledBy);
            w.WriteInt(killerNetId);
            w.End();
            NetManager.Instance.SendTo(victim.OwnerId, w);
        }

private void SendKnockback(NetIdentity victim, Vector3 dir, float force)
        {
            NetManager net = NetManager.Instance;

            if (victim.OwnerId == NetHost.HOST_ID)
            {
                ApplyKnockbackLocal(victim.NetId, dir, force);
                return;
            }

            w.Begin(MsgType.Knockback);
            w.WriteInt(victim.NetId);
            w.WriteFloat(dir.x);
            w.WriteFloat(dir.z);
            w.WriteFloat(force);
            w.End();
            net.SendTo(victim.OwnerId, w);
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
                pm.ApplyKnockback(dir, force);
            else
            {
                AIPlayerMovement bot = victim.Bot;
                if (bot != null)
                    bot.ApplyKnockbackFromNet(dir.x, dir.z, force);
                else
                {
                    NetKnockback kb = victim.GetComponent<NetKnockback>();
                    if (kb != null)
                        kb.Apply(dir, force);
                }
            }
        }
    }
}
