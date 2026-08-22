using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    //최종 순위를 세우고 패킷으로 싣고 푸는 일만 한다
    //호스트가 만든 순서를 클라가 그대로 받아야 해서 정렬은 여기 한 곳에서만 일어난다
    public static class LanStandings
    {
        public const int MAX_ENTRIES = 20;

        public struct Result
        {
            public List<LanScoreboard.Entry> Entries;
            public int WinnerNetId;
            public string WinnerName;
            public int WinnerScore;
        }

        public static Result Build(NetIdentity winner)
        {
            List<LanScoreboard.Entry> entries = LanScoreboard.Collect();

            if (winner == null && entries.Count > 0)
                winner = Find(entries[0].netId);

            Result result;
            result.Entries = entries;
            result.WinnerNetId = winner != null ? winner.NetId : 0;

            int index = IndexOf(entries, result.WinnerNetId);

            result.WinnerName = index >= 0 ? entries[index].name : NameOf(winner);

            //우승자를 0번으로 끌어올린다. 크기순 정렬이라 생존 우승자가 1위가 아닐 수 있다
            if (index > 0)
            {
                LanScoreboard.Entry w = entries[index];
                entries.RemoveAt(index);
                entries.Insert(0, w);
            }

            LanPlayerState state = winner != null ? winner.PlayerState : null;

            if (state != null)
                result.WinnerScore = state.Score;
            else
                result.WinnerScore = entries.Count > 0 ? entries[0].score : 0;

            return result;
        }

        public static void Write(NetWriter w, List<LanScoreboard.Entry> entries, string winnerName)
        {
            int n = Mathf.Min(entries.Count, MAX_ENTRIES);

            w.Begin(MsgType.FinalStandings);
            w.WriteString(winnerName);
            w.WriteByte((byte)n);

            for (int i = 0; i < n; i++)
            {
                LanScoreboard.Entry e = entries[i];

                w.WriteString(e.name);
                w.WriteByte(e.isBot ? (byte)1 : (byte)0);
                w.WriteInt(e.netId);
                w.WriteInt(e.ownerId);
                w.WriteFloat(e.scale);
                w.WriteInt(e.score);
                w.WriteFloat(e.color.r);
                w.WriteFloat(e.color.g);
                w.WriteFloat(e.color.b);
            }

            w.End();
        }

        public static void Read(NetReader r)
        {
            string winnerName = r.ReadString();
            int n = r.ReadByte();

            List<LanScoreboard.Entry> entries = new List<LanScoreboard.Entry>(n);
            int myId = NetManager.Instance != null ? NetManager.Instance.MyId : 0;

            for (int i = 0; i < n; i++)
            {
                LanScoreboard.Entry e;
                e.name = r.ReadString();
                e.isBot = r.ReadByte() != 0;
                e.netId = r.ReadInt();
                e.ownerId = r.ReadInt();
                e.scale = r.ReadFloat();
                e.score = r.ReadInt();

                float cr = r.ReadFloat();
                float cg = r.ReadFloat();
                float cb = r.ReadFloat();
                e.color = new Color(cr, cg, cb, 1f);

                e.isLocal = !e.isBot && e.ownerId == myId;

                entries.Add(e);
            }

            LanScoreboard.SetFinal(entries, winnerName);
        }

        public static string NameOf(NetIdentity id)
        {
            if (id == null)
                return "";

            LanPlayerState ps = id.PlayerState;
            if (ps != null && !string.IsNullOrEmpty(ps.PlayerName))
                return ps.PlayerName;

            LanBotState bs = id.BotState;
            if (bs != null && !string.IsNullOrEmpty(bs.BotName))
                return bs.BotName;

            return ps != null ? ("P" + ps.OwnerId) : ("net" + id.NetId);
        }

        private static int IndexOf(List<LanScoreboard.Entry> entries, int netId)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].netId == netId)
                    return i;

            return -1;
        }

        private static NetIdentity Find(int netId)
        {
            return NetWorld.Instance != null ? NetWorld.Instance.Find(netId) : null;
        }
    }
}
