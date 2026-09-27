using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public static class NetEntity
    {
        private static float baselineScale = -1f;

        private static bool IsHostNow
        {
            get { return NetManager.Instance != null && NetManager.Instance.IsHost; }
        }

        public static bool IsDrivenElsewhere(Component c)
        {
            if (c == null)
                return false;

            NetIdentity id = c.GetComponentInParent<NetIdentity>();

            if (id == null)
                return false;

            if (id.NetId >= NetConfig.SCENE_ID_BASE)
                return false;

            return !id.IsSimulatedHere;
        }

        public static bool DrivesScaleHere(Component c)
        {
            if (c == null)
                return false;

            LanBotState bot = c.GetComponentInParent<LanBotState>();

            if (bot == null)
                return true;

            return bot.IsDriver;
        }

        public static float BaselineScale
        {
            get
            {
                if (baselineScale > 0f)
                    return baselineScale;

                NetWorld world = NetWorld.Instance;

                if (world == null || world.prefabs == null || world.prefabs.Length == 0 || world.prefabs[0] == null)
                    return 1f;

                baselineScale = world.prefabs[0].transform.localScale.x;
                return baselineScale;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBaseline() => baselineScale = -1f;

        public static int ScoreFromScale(float scale)
        {
            DataManager rules = DataManager.Instance;

            if (rules == null || rules.JellyScaleIncrease <= 0f)
                return 0;

            float grown = scale - BaselineScale;
            return Mathf.Max(0, Mathf.RoundToInt(grown * rules.ScorePerJelly / rules.JellyScaleIncrease));
        }

        public static bool IsJelly(NetIdentity id)
        {
            if (id == null)
                return false;

            if (id.IsBot)
                return false;

            return id.PrefabId >= NetConfig.JELLY_PREFAB_START;
        }

        public static bool IsOutOfPlay(NetIdentity id)
        {
            if (id == null)
                return true;

            INetEntity e = EntityOf(id);
            return e != null && e.IsOutOfPlay;
        }

        public static INetEntity EntityOf(NetIdentity id)
        {
            if (id == null)
                return null;

            if (id.PlayerState != null)
                return id.PlayerState;

            return id.BotState;
        }

        public static float ScaleOf(NetIdentity id)
        {
            INetEntity e = EntityOf(id);

            if (e != null)
                return e.ScaleValue;

            return id != null ? id.transform.localScale.x : 1f;
        }

        public static void AddScore(NetIdentity id, int delta)
        {
            if (delta == 0)
                return;

            INetEntity e = EntityOf(id);

            if (e != null)
                e.HostAddScore(delta);
        }

        public static void SetScore(NetIdentity id, int score)
        {
            if (id == null)
                return;

            LanPlayerState state = id.PlayerState;

            if (state != null)
            {
                state.HostSetScore(score);
                return;
            }

            LanBotState bot = id.BotState;

            if (bot != null)
                bot.HostSetScore(score);
        }

        public static void HostSetScoreFromScale(NetIdentity id)
        {
            if (id == null || DataManager.Instance == null)
                return;

            SetScore(id, ScoreFromScale(ScaleOf(id)));
        }

        public static void HostEliminate(NetIdentity id)
        {
            if (!NetManager.Offline && !IsHostNow)
                return;
            if (id == null || IsOutOfPlay(id))
                return;

            if (LanGameFlow.Instance != null && LanGameFlow.Instance.Phase != GamePhase.Playing)
                return;

            if (LanScoreboard.CountAlive() <= 1)
            {
                LanGameFlow.Instance?.HostDeclareLastSurvivor(id);
                return;
            }

            if (PushMode.Instance != null)
                PushMode.Instance.HostAwardKillCredit(id.NetId);

            LanPlayerState state = id.PlayerState;

            if (state != null)
            {
                state.HostSetFlag(PlayerFlags.Eliminated, true);
                return;
            }

            LanBotState bot = id.BotState;

            if (bot != null)
                bot.HostEliminate();

            LanGameFlow.Instance?.HostCheckEndNow();
        }

        public static int ScoreOf(NetIdentity id)
        {
            INetEntity e = EntityOf(id);
            return e != null ? e.Score : 0;
        }

        public static bool IsSameSide(NetIdentity a, NetIdentity b)
        {
            if (a == null || b == null)
                return false;

            if (a == b)
                return true;

            if (a.IsBot || b.IsBot)
                return false;

            return a.OwnerId == b.OwnerId;
        }

        public static void CollectCharacters(List<NetIdentity> into)
        {
            into.Clear();

            IReadOnlyList<INetEntity> entities = EntityRegistry.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                INetEntity e = entities[i];
                if (e != null && e.Identity != null)
                    into.Add(e.Identity);
            }
        }
    }
}
