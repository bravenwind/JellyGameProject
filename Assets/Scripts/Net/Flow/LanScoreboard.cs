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

        // ★ 예전엔 사람용·봇용 두 벌 루프였다
        //   같은 Entry를 채우는데 읽는 프로퍼티 이름만 달랐다
        //   (PlayerName/BotName, Score/CurrentScore, VisualColor/ReadVisualColor…).
        //   그래서 한쪽에만 고친 게 실제로 있었다 — 봇 점수를 방송하지 않아
        //   클라 순위표에서 봇이 0점으로 보였다. 이제 INetEntity 한 벌로 돈다.
        public static List<Entry> Collect(bool includeDead = false)
        {
            List<Entry> list = new List<Entry>();

            IReadOnlyList<INetEntity> entities = EntityRegistry.Entities;

            for (int i = 0; i < entities.Count; i++)
            {
                INetEntity en = entities[i];

                //MonoBehaviour가 파괴된 뒤에도 인터페이스 참조는 남는다.
                //Identity(UnityEngine.Object)로 확인해야 페이크 널이 걸린다
                if (en == null || en.Identity == null)
                    continue;
                if (!includeDead && en.IsOutOfPlay)
                    continue;

                Entry e;
                e.name = en.DisplayName;
                e.isBot = en.IsBot;
                e.netId = en.EntityId;
                e.ownerId = en.OwnerId;
                e.scale = en.ScaleValue;
                e.score = en.Score;
                e.color = en.VisualColor;
                e.isLocal = en.Identity.IsMine;
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

        /// <summary>살아 있는 참가자 수. 목록이 필요 없을 때 List 생성을 피한다.</summary>
        public static int CountAlive()
        {
            IReadOnlyList<INetEntity> entities = EntityRegistry.Entities;
            int n = 0;

            for (int i = 0; i < entities.Count; i++)
            {
                INetEntity e = entities[i];

                if (e == null || e.Identity == null || e.IsOutOfPlay)
                    continue;
                n++;
            }

            return n;
        }

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
