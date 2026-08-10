using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public static class LanScoreboard
    {
        public struct Entry
        {
            public string name;
            public bool isBot;
            public int netId;
            public int ownerId;
            public float scale;
            public int score;
            public Color color;
            public bool isLocal;
        }

        public static List<Entry> Collect(bool includeDead = false)
        {
            List<Entry> list = new List<Entry>();

            IReadOnlyList<LanPlayerState> players = EntityRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                LanPlayerState p = players[i];
                if (p == null)
                    continue;
                if (!includeDead && p.IsOutOfPlay)
                    continue;

                Entry e;
                e.name = string.IsNullOrEmpty(p.PlayerName) ? ("P" + p.OwnerId) : p.PlayerName;
                e.isBot = false;
                e.netId = p.EntityId;
                e.ownerId = p.OwnerId;
                e.scale = p.ScaleValue;
                e.score = p.Score;
                e.color = p.VisualColor;
                e.isLocal = p.IsMine;
                list.Add(e);
            }

            IReadOnlyList<AIPlayerMovement> bots = EntityRegistry.Bots;
            for (int i = 0; i < bots.Count; i++)
            {
                AIPlayerMovement b = bots[i];
                if (b == null)
                    continue;
                if (!includeDead && b.IsOutOfPlay)
                    continue;

                LanBotSync bs = b.GetComponent<LanBotSync>();

                Entry e;
                e.name = bs != null && !string.IsNullOrEmpty(bs.BotName)
                    ? bs.BotName
                    : ("AI 봇 " + NetIdentity.IdOf(b));
                e.isBot = true;
                e.netId = NetIdentity.IdOf(b);
                e.ownerId = 0;
                e.scale = b.GetMyAuthorityScale();
                e.score = bs != null ? bs.CurrentScore : 0;
                e.color = bs != null ? bs.ReadVisualColor() : Color.white;
                e.isLocal = false;
                list.Add(e);
            }

            list.Sort(CompareByScaleDesc);
            return list;
        }

        private static int CompareByScaleDesc(Entry a, Entry b)
        {
            int c = b.scale.CompareTo(a.scale);
            if (c != 0)
                return c;
            return a.netId.CompareTo(b.netId);
        }

        public static List<Entry> FinalStandings { get; private set; }

        public static string WinnerName { get; private set; }

        public static void SetFinal(List<Entry> entries, string winner)
        {
            FinalStandings = entries;
            WinnerName = winner ?? "";
        }

        public static void Clear()
        {
            FinalStandings = null;
            WinnerName = "";
        }
    }
}
