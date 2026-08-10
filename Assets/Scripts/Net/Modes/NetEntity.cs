using UnityEngine;

namespace JellyNet
{
    //네트워크 개체를 사람·봇 구분 없이 다루기 위한 질의 모음
    //같은 판단이 AbsorbMode와 PushMode에 따로 있다가 한쪽만 고쳐지는 일이 반복됐다
    public static class NetEntity
    {
        public static bool IsJelly(NetIdentity id)
        {
            if (id == null)
                return false;

            if (id.IsBot)
                return false;

            return id.PrefabId >= NetConfig.JELLY_PREFAB_START;
        }

        //사람은 LanPlayerState, 봇은 AIPlayerMovement가 판 밖 여부를 들고 있다
        public static bool IsOutOfPlay(NetIdentity id)
        {
            if (id == null)
                return true;

            if (id.IsBot)
            {
                AIPlayerMovement bot = id.GetComponent<AIPlayerMovement>();
                return bot != null && bot.IsOutOfPlay;
            }

            LanPlayerState state = id.GetComponent<LanPlayerState>();
            return state != null && state.IsOutOfPlay;
        }

        //판정 기준이 되는 실제 크기. NetScale이 아니라 게임 쪽 값을 본다
        public static float ScaleOf(NetIdentity id)
        {
            if (id == null)
                return 1f;

            LanPlayerVisual visual = id.GetComponent<LanPlayerVisual>();

            if (visual != null && visual.HasScaleController)
                return visual.ScaleValue;

            NetScale scale = id.GetComponent<NetScale>();
            return scale != null ? scale.Current : 1f;
        }

        public static string NameOf(NetIdentity id)
        {
            if (id == null)
                return string.Empty;

            LanPlayerState state = id.GetComponent<LanPlayerState>();

            if (state != null && !string.IsNullOrEmpty(state.PlayerName))
                return state.PlayerName;

            LanBotSync bot = id.GetComponent<LanBotSync>();

            if (bot != null && !string.IsNullOrEmpty(bot.BotName))
                return bot.BotName;

            return state != null ? $"P{state.OwnerId}" : $"net{id.NetId}";
        }

        public static void AddScore(NetIdentity id, int delta)
        {
            if (id == null || delta == 0)
                return;

            LanPlayerState state = id.GetComponent<LanPlayerState>();

            if (state != null)
            {
                state.HostAddScore(delta);
                return;
            }

            LanBotSync bot = id.GetComponent<LanBotSync>();

            if (bot != null)
                bot.HostAddScore(delta);
        }

        public static int ScoreOf(NetIdentity id)
        {
            if (id == null)
                return 0;

            LanPlayerState state = id.GetComponent<LanPlayerState>();

            if (state != null)
                return state.Score;

            LanBotSync bot = id.GetComponent<LanBotSync>();
            return bot != null ? bot.CurrentScore : 0;
        }

        //봇은 전부 호스트 소유라 OwnerId 비교만으로는 같은 편으로 오인된다
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

        public static NetIdentity FindMyPlayer()
        {
            if (NetWorld.Instance == null)
                return null;

            foreach (var pair in NetWorld.Instance.Objects)
            {
                NetIdentity id = pair.Value;

                if (id == null || id.IsBot || IsJelly(id))
                    continue;

                if (id.IsMine)
                    return id;
            }

            return null;
        }
    }
}
