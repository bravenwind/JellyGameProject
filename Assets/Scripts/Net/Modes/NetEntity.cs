using System.Collections.Generic;
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
                AIPlayerMovement bot = id.Bot;
                return bot != null && bot.IsOutOfPlay;
            }

            LanPlayerState state = id.PlayerState;
            return state != null && state.IsOutOfPlay;
        }

        // ★ 크기를 묻는 유일한 창구
        //   예전엔 네 곳이 각자 답을 만들었다.
        //     NetEntity.ScaleOf            → LanPlayerVisual.ScaleValue
        //     LanPlayerState.ScaleValue    → PlayerScaleController.currentScaleValue
        //     LanPlayerVisual.ScaleValue   → PlayerScaleController.currentScaleValue
        //     AIPlayerMovement.GetMyAuthorityScale → transform.localScale.x
        //   앞의 셋은 '논리적 크기'(연출이 끝난 목표값)이고 마지막 하나만
        //   '지금 화면에 보이는 크기'였다. 커지는 연출이 도는 0.3초 동안 봇만
        //   다른 답을 내놓아서, 흡수 가능 여부가 보는 쪽마다 갈렸다.
        //   이제 전부 PlayerScaleController를 거친다 — 없을 때만 transform으로 떨어진다.
        public static float ScaleOf(NetIdentity id)
        {
            if (id == null)
                return 1f;

            LanPlayerVisual visual = id.Visual;

            if (visual != null && visual.HasScaleController)
                return visual.ScaleValue;

            //봇 프리팹에 LanPlayerVisual이 없거나 스케일 컨트롤러가 빠진 경우의 마지막 보루
            return id.transform.localScale.x;
        }

        public static void AddScore(NetIdentity id, int delta)
        {
            if (id == null || delta == 0)
                return;

            LanPlayerState state = id.PlayerState;

            if (state != null)
            {
                state.HostAddScore(delta);
                return;
            }

            LanBotState bot = id.BotState;

            if (bot != null)
                bot.HostAddScore(delta);
        }

        public static int ScoreOf(NetIdentity id)
        {
            if (id == null)
                return 0;

            LanPlayerState state = id.PlayerState;

            if (state != null)
                return state.Score;

            LanBotState bot = id.BotState;
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

        /// <summary>
        /// 사람 + 봇만 모은다. 호출부의 List를 재사용하므로 할당이 없다.
        ///
        /// ★ NetWorld.Objects를 순회하면 안 되는 이유
        ///   거기엔 젤리 30여 개와 씬에 배치된 소품(캔디 300개 등)까지 들어 있다.
        ///   캐릭터 몇 개를 찾으려고 수백 개를 훑고 매번 IsJelly로 걸러내게 된다.
        ///   EntityRegistry는 종류별로 이미 나뉘어 있다.
        /// </summary>
        public static void CollectCharacters(List<NetIdentity> into)
        {
            into.Clear();

            IReadOnlyList<LanPlayerState> players = EntityRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                LanPlayerState p = players[i];
                if (p != null && p.Identity != null)
                    into.Add(p.Identity);
            }

            IReadOnlyList<AIPlayerMovement> bots = EntityRegistry.Bots;
            for (int i = 0; i < bots.Count; i++)
            {
                AIPlayerMovement b = bots[i];
                if (b != null && b.Identity != null)
                    into.Add(b.Identity);
            }
        }

        public static NetIdentity FindMyPlayer()
        {
            IReadOnlyList<LanPlayerState> players = EntityRegistry.Players;

            for (int i = 0; i < players.Count; i++)
            {
                LanPlayerState p = players[i];
                if (p != null && p.IsMine)
                    return p.Identity;
            }

            return null;
        }
    }
}
