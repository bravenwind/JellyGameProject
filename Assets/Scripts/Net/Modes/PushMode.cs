using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class PushMode : NetGameMode<PushMode>
    {
        protected override GameModeType Mode
        {
            get { return GameModeType.Push; }
        }

        [Header("검증 여유")]
        [Tooltip("호스트 재검증 시 사거리에 곱하는 여유. 지연을 감안해 넉넉히.")]
        public float rangeTolerance = 1.6f;

        //배트 수치는 DataManager 하나에서만 온다. 예전엔 인스펙터 사본이 따로 있어
        //둘이 벌어지면서 밀치기 힘이 의도의 절반만 나오는 일이 있었다
        private static float BatRange   => DataManager.Instance != null ? DataManager.Instance.BatRange : 1.6f;
        private static float BatPushForce => DataManager.Instance != null ? DataManager.Instance.BatPushForce : 8f;
        private static float BatHitGrowth => DataManager.Instance != null ? DataManager.Instance.BatHitGrowth : 0.06f;

        [Header("점수")]
        [Tooltip("밀치기에 성공할 때마다 얻는 점수.")]
        public int pushHitScore = 100;

        [Tooltip("내가 민 상대가 이 시간 안에 떨어지면 '내가 떨어뜨린 것'으로 본다(초).")]
        public float killAssistSeconds = 5f;

        private readonly NetWriter w = new NetWriter();

        protected override void ResetAll()
        {
            lastPusher.Clear();
        }

        /// <summary>
        /// 봇이 휘둘렀을 때. 봇은 호스트에서만 생각하므로 소켓을 거치지 않는다.
        ///
        /// ★ 봇 전용이다 — 사람은 RequestBatHit으로 가야 한다
        ///   여기서 requesterId로 HOST_ID를 넘기는데, HostJudgement가
        ///   actor.OwnerId != requesterId 면 거절한다. 봇은 전부 호스트 소유라
        ///   성립하지만, 클라의 사람을 태우면 조용히 거절돼 배트가 안 먹는 것처럼 보인다.
        /// </summary>
        public void HostBotBatHit(int victimNetId, int botNetId)
        {
            if (!IsHost)
                return;

            ResolveBatHit(NetHost.HOST_ID, victimNetId, botNetId);
        }

        /// <summary>사람이 휘둘렀을 때. 호스트면 바로 판정하고, 클라면 판정을 요청한다.</summary>
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
            net.Client.Send(w);
        }

        protected override void RegisterRoutes()
        {
            Net.RouteHost(MsgType.BatHitRequest, (from, r) =>
            {
                int victimNetId = r.ReadInt();
                int attackerNetId = r.ReadInt();
                ResolveBatHit(from.Id, victimNetId, attackerNetId);
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

            float aScale = NetEntity.ScaleOf(attacker);
            float vScale = NetEntity.ScaleOf(victim);

            if (!judgement.WithinReach((BatRange * aScale + vScale) * rangeTolerance))
                return;

            float startScale = DataManager.Instance != null ? DataManager.Instance.StartingScale : 1f;
            float force = BatPushForce * (aScale / Mathf.Max(0.01f, startScale));

            SendKnockback(victim, judgement.DirectionToTarget(), force);

            float growth = BatHitGrowth / Mathf.Max(aScale, 1f);

            NetWorld.Instance.BroadcastGrow(attackerNetId, GrowKind.BatHit, growth);

            NetEntity.AddScore(attacker, pushHitScore);

            lastPusher[victimNetId] = new Credit { AttackerNetId = attackerNetId, At = Time.time };

            NetManager.Instance.AddLog($"P{attacker.OwnerId} → P{victim.OwnerId} 배트 히트! (+{pushHitScore})");
        }

        struct Credit
        { 
            public int AttackerNetId; 
            public float At;
        }

        private readonly Dictionary<int, Credit> lastPusher = new Dictionary<int, Credit>();

        //탈락 자체를 처리하는 게 아니라, 최근에 민 사람에게 킬 점수를 넘겨주는 정산이다.
        //LanGameFlow.HostConfirmEliminated(탈락 확정)와 헷갈리지 않게 이름을 나눴다
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

            //관전 시작 화면을 가해자로 맞춰주기 위해 피해자에게만 알린다
            SendKilledBy(victim, c.AttackerNetId);

            int stolen = NetEntity.ScoreOf(victim);
            if (stolen <= 0)
                return;

            NetEntity.AddScore(attacker, stolen);
            net.AddLog("P" + attacker.OwnerId + " 가 떨어뜨림 — 점수 " + stolen + " 획득");
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

            NetHost.Peer target = NetManager.Instance.Host.FindPeer(victim.OwnerId);
            if (target == null)
                return;

            w.Begin(MsgType.KilledBy);
            w.WriteInt(killerNetId);
            w.End();
            NetManager.Instance.Host.SendTo(target, w);
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
                    //사람도 봇도 아닌 네트워크 오브젝트(씬에 놓인 캔디 등)용.
                    //물리를 안 거치고 transform을 직접 옮기므로 벽을 뚫는다 — 배경 소품에만 쓸 것
                    NetKnockback kb = victim.GetComponent<NetKnockback>();
                    if (kb != null)
                        kb.Apply(dir, force);
                }
            }

            NetManager.Instance.AddLog("밀려남! (힘 " + force.ToString("F1") + ")");
        }


    }
}
