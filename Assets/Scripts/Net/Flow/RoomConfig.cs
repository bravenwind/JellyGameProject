using UnityEngine;

namespace JellyNet
{
    public static class RoomConfig
    {
        public static bool HasValue { get; private set; }

        public static GameModeType Mode { get; private set; } = GameModeType.Absorb;

        public static int TotalPlayers { get; private set; } = 4;

        public static int AiCount { get; private set; } = 2;

        public static string Nickname = "";

        public static int HumanCount
        {
            get { return Mathf.Max(1, TotalPlayers - AiCount); }
        }

        public static void Set(GameModeType mode, int totalPlayers, int aiCount)
        {
            Mode = mode;
            TotalPlayers = Mathf.Max(1, totalPlayers);
            AiCount = Mathf.Clamp(aiCount, 0, TotalPlayers - 1);
            HasValue = true;
        }

        public static void Clear()
        {
            HasValue = false;
        }

        // ★ LobbyFlow 에 있던 것을 옮겼다
        //   모드를 사람이 읽는 글자로 바꾸는 순수 함수인데 로비 화면에 얹혀 있었다.
        //   그래서 방 목록의 한 줄(LanRoomRow)이 이 한 줄 때문에 LobbyFlow 를
        //   참조하고 있었다 — 줄은 자기가 그리는 것 말고는 아무것도 몰라야 한다.
        //   모드가 어떤 이름으로 불리는지는 방 설정의 일이므로 여기가 제자리다.
        public static string ModeLabel(GameModeType mode)
        {
            return mode == GameModeType.Push ? "밀치기" : "흡수";
        }
    }
}
