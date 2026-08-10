using UnityEngine;

namespace JellyNet
{
    //호스트가 클라의 주장을 검증할 때 매번 통과해야 하는 전제
    //흡수·밀치기가 같은 순서를 각자 적어두고 있어 한쪽만 고쳐지는 일이 있었다
    public readonly struct HostVerdict
    {
        public readonly NetIdentity Actor;
        public readonly NetIdentity Target;
        public readonly bool Valid;

        private HostVerdict(NetIdentity actor, NetIdentity target, bool valid)
        {
            Actor = actor;
            Target = target;
            Valid = valid;
        }

        //actorNetId를 주장한 사람이 requesterId다. 소유권까지 여기서 확인한다
        public static HostVerdict Judge(GameModeType mode, int requesterId, int actorNetId, int targetNetId)
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

            //남의 캐릭터를 내세운 요청 차단
            if (actor.OwnerId != requesterId)
                return Reject();

            if (NetEntity.IsSameSide(actor, target))
                return Reject();

            if (NetEntity.IsOutOfPlay(actor) || NetEntity.IsOutOfPlay(target))
                return Reject();

            return new HostVerdict(actor, target, true);
        }

        //지연을 감안해 넉넉히 잡는다. 클라의 거리 판정을 그대로 믿지 않되 정상 플레이는 막지 않는다
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

        private static HostVerdict Reject()
        {
            return new HostVerdict(null, null, false);
        }
    }
}
