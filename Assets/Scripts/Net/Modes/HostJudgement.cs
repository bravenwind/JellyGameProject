using UnityEngine;

namespace JellyNet
{
    public readonly struct HostJudgement
    {
        public readonly NetIdentity Actor;
        public readonly NetIdentity Target;
        public readonly bool Valid;

        private HostJudgement(NetIdentity actor, NetIdentity target, bool valid)
        {
            Actor = actor;
            Target = target;
            Valid = valid;
        }

        public static HostJudgement Judge(GameModeType mode, int requesterId, int actorNetId, int targetNetId)
        {
            NetManager net = NetManager.Instance;

            if (net == null || !net.IsHost || NetWorld.Instance == null)
                return Reject();

            if (!LanGameFlow.IsPlaying(mode))
                return Reject();

            NetIdentity actor = NetWorld.Instance.Find(actorNetId);
            NetIdentity target = NetWorld.Instance.Find(targetNetId);

            if (actor == null || target == null)
                return Reject();

            if (actor.OwnerId != requesterId)
                return Reject();

            if (NetEntity.IsSameSide(actor, target))
                return Reject();

            if (NetEntity.IsOutOfPlay(actor) || NetEntity.IsOutOfPlay(target))
                return Reject();

            return new HostJudgement(actor, target, true);
        }

        public bool WithinReach(float reach)
        {
            if (!Valid)
                return false;

            Vector3 gap = Target.transform.position - Actor.transform.position;
            gap.y = 0f;

            return gap.sqrMagnitude <= reach * reach;
        }

        public Vector3 DirectionToTarget()
        {
            Vector3 gap = Target.transform.position - Actor.transform.position;
            gap.y = 0f;

            if (gap.sqrMagnitude < 0.01f)
                return Actor.transform.forward;

            return gap.normalized;
        }

        private static HostJudgement Reject()
        {
            return new HostJudgement(null, null, false);
        }
    }
}
