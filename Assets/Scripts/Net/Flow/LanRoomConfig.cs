using UnityEngine;

namespace JellyNet
{
    public static class LanRoomConfig
    {
        public static bool HasValue { get; private set; }

        public static GameModeType Mode { get; private set; } = GameModeType.Absorb;

        public static int TotalPlayers { get; private set; } = 4;

        public static int AiCount { get; private set; } = 2;

        public static int HumanCount
        {
            get { return Mathf.Max(1, TotalPlayers - AiCount); }
        }

        public static string Nickname = "";

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
    }
}
