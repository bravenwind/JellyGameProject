// ============================================================
// ScoreboardSnapshot.cs
// ============================================================
// [H6] 인게임 리더보드(GameModeManager.GetSortedScores)와 결과 씬
// (GameResultManager.GatherTopEntries)에 따로 구현돼 있던
// "현재 방의 플레이어+봇 점수판 수집" 로직을 한 곳으로 통일한 헬퍼.
//
// 두 호출처는 '색을 어디서 읽는가'만 다르다:
//   • 인게임  : 살아있는 오브젝트(EntityRegistry)의 머티리얼/DisplayColor
//   • 결과 씬 : 룸/플레이어 커스텀 프로퍼티(Color_R/G/B) — 오브젝트는 이미 파괴됨
// 그래서 색 조회만 델리게이트로 받고, 수집·탈락 필터·정렬은 여기서 단일 기준으로 처리한다.
// (한쪽만 고쳐서 인게임 순위와 최종 결과가 어긋나는 사고를 구조적으로 차단)
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public static class ScoreboardSnapshot
{
    public struct Entry
    {
        public string name;
        public bool isBot;
        public int actorNumber; // 플레이어 식별자 (봇은 0) — 닉네임 중복에 안전한 비교용
        public int botViewId;   // 봇 식별자 (플레이어는 0)
        public float scale;
        public Color color;
    }

    /// <summary>
    /// 방의 모든 생존 플레이어+봇을 수집해 scale 내림차순으로 반환한다.
    /// </summary>
    /// <param name="defaultScale">"Scale" 프로퍼티가 아직 없을 때 쓸 기본값.</param>
    /// <param name="playerColorResolver">플레이어 색 조회. null이면 흰색.</param>
    /// <param name="botColorResolver">봇 색 조회 (prefix 예: "Bot1001", viewId). null이면 흰색.</param>
    /// <param name="survivorActors">Push 모드 마스터 권위 생존자 목록.
    /// null이면 각 플레이어의 "Eliminated" 자기보고 프로퍼티로 필터한다.</param>
    /// <param name="includeEliminatedPlayers">true면 플레이어 탈락 필터를 끈다
    /// (동시 탈락으로 결과가 비는 것을 막는 폴백 전용. 봇 필터에는 영향 없음).</param>
    public static List<Entry> Collect(
        float defaultScale,
        Func<Player, Color> playerColorResolver,
        Func<string, int, Color> botColorResolver,
        HashSet<int> survivorActors = null,
        bool includeEliminatedPlayers = false)
    {
        var entries = new List<Entry>();
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return entries;

        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (!includeEliminatedPlayers && IsPlayerEliminated(p, survivorActors)) continue;

            entries.Add(new Entry
            {
                name = p.NickName,
                isBot = false,
                actorNumber = p.ActorNumber,
                scale = ReadFloat(p.CustomProperties, "Scale", defaultScale),
                color = playerColorResolver?.Invoke(p) ?? Color.white,
            });
        }

        var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
        foreach (var key in roomProps.Keys)
        {
            string keyStr = key.ToString();
            if (!keyStr.EndsWith("_Name")) continue;

            object nameVal = roomProps[keyStr];
            if (nameVal == null) continue;

            string prefix = keyStr.Substring(0, keyStr.Length - "_Name".Length);
            int viewId = 0;
            if (prefix.StartsWith("Bot"))
                int.TryParse(prefix.Substring(3), out viewId);

            // 살아있는 봇 오브젝트가 있으면 그 탈락 플래그를 우선 신뢰한다.
            // (탈락 봇의 룸 프로퍼티 정리는 비동기라 잠깐 남아 있을 수 있음.
            //  결과 씬처럼 봇 오브젝트가 이미 파괴된 상황에서는 이 검사가 그냥 통과되고,
            //  프로퍼티 정리 여부가 곧 생존 여부가 된다 — 기존 두 구현의 동작을 모두 포섭)
            if (viewId != 0 && IsBotEliminated(viewId)) continue;

            entries.Add(new Entry
            {
                name = nameVal.ToString(),
                isBot = true,
                botViewId = viewId,
                scale = ReadFloat(roomProps, prefix + "_Scale", defaultScale),
                color = botColorResolver?.Invoke(prefix, viewId) ?? Color.white,
            });
        }

        return entries.OrderByDescending(e => e.scale).ToList();
    }

    private static bool IsPlayerEliminated(Player p, HashSet<int> survivorActors)
    {
        if (survivorActors != null)
            return !survivorActors.Contains(p.ActorNumber);
        return p.CustomProperties.TryGetValue("Eliminated", out object e) && e is bool b && b;
    }

    private static bool IsBotEliminated(int viewId)
    {
        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot != null && bot.photonView != null && bot.photonView.ViewID == viewId)
                return bot.IsEliminated;
        }
        return false;
    }

    private static float ReadFloat(ExitGames.Client.Photon.Hashtable props, string key, float fallback)
    {
        if (props != null && props.TryGetValue(key, out object v) && v is float f) return f;
        return fallback;
    }
}
