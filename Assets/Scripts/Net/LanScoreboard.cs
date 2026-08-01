using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 순위 수집. ScoreboardSnapshot(Photon 룸 프로퍼티판)을 대신한다.
    ///
    /// ═══════════════════════════════════════════════════════
    ///  ★ 왜 통째로 새로 쓰는가
    /// ═══════════════════════════════════════════════════════
    ///
    ///  원본은 순위를 <b>룸 커스텀 프로퍼티</b>에서 읽었다.
    ///    "Bot17_Score", "Bot17_Scale", 플레이어의 "Scale"/"Eliminated" ...
    ///
    ///  그렇게 한 이유는 하나였다 — 결과 씬으로 넘어가면 게임 오브젝트가
    ///  전부 파괴되므로, 순위를 오브젝트 밖 어딘가에 보관해야 했다.
    ///  그 대가로 인게임 순위까지 문자열 키 조회로 돌아가고 있었다.
    ///
    ///  LAN에서는 그럴 이유가 없다.
    ///    · 인게임 순위 → 눈앞의 오브젝트(EntityRegistry)에서 바로 읽는다
    ///    · 결과 씬     → 게임이 끝나는 순간 한 번 스냅샷을 떠서 넘긴다
    ///
    ///  즉 "살아있는 동안은 실물에서, 끝나는 순간 한 장만 박제한다."
    /// </summary>
    public static class LanScoreboard
    {
        public struct Entry
        {
            public string name;
            public bool isBot;
            public int netId;      // 개체 식별자 (이름 중복에 안전)
            public int ownerId;    // 사람 플레이어의 소유자 번호 (봇은 0)
            public float scale;
            public int score;
            public Color color;
            public bool isLocal;   // 내 것인가 (결과 화면 강조용)
        }

        /// <summary>
        /// 지금 살아있는 플레이어+봇을 크기 내림차순으로 모은다.
        ///
        /// ★ 정렬 기준을 점수가 아니라 크기로 두는 이유
        ///   원본과 같다. 점수는 크기에서 계산되는 값이라 순서가 같고,
        ///   크기는 매 프레임 정확한 반면 점수는 갱신 시점이 한 박자 늦다.
        /// </summary>
        public static List<Entry> Collect(bool includeDead = false)
        {
            List<Entry> list = new List<Entry>();

            // ── 사람 ──
            IReadOnlyList<LanPlayerState> players = EntityRegistry.Players;
            for (int i = 0; i < players.Count; i++)
            {
                LanPlayerState p = players[i];
                if (p == null) continue;
                if (!includeDead && p.IsOutOfPlay) continue;

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

            // ── 봇 ──
            IReadOnlyList<AIPlayerMovement> bots = EntityRegistry.Bots;
            for (int i = 0; i < bots.Count; i++)
            {
                AIPlayerMovement b = bots[i];
                if (b == null) continue;
                if (!includeDead && b.IsOutOfPlay) continue;

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

        static int CompareByScaleDesc(Entry a, Entry b)
        {
            int c = b.scale.CompareTo(a.scale);
            if (c != 0) return c;
            return a.netId.CompareTo(b.netId);   // 동점이면 순서를 고정한다(화면 깜빡임 방지)
        }

        // ═════════════════════════════════════════════
        //  결과 씬으로 넘기는 최종 스냅샷
        // ═════════════════════════════════════════════
        //
        // ★ 왜 static에 담아 넘기는가
        //   결과 씬에서는 소켓이 필요 없다. 게임이 끝나는 순간 호스트가 순위를
        //   한 번 방송하고, 각자 그걸 받아 여기 담아둔 뒤 씬을 넘긴다.
        //   그래서 씬 전환 도중 연결이 끊겨도 결과는 정상적으로 보인다.
        //   (소켓을 결과 씬까지 끌고 가면 그 끊김 처리가 훨씬 까다로워진다)

        /// <summary>게임 종료 시점에 확정된 순위. 결과 씬이 이걸 읽는다.</summary>
        public static List<Entry> FinalStandings { get; private set; }

        /// <summary>승자 이름(표시용).</summary>
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
