using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 로비에서 정한 방 설정을 게임 씬으로 나르는 그릇.
    ///
    /// ★ 왜 static인가
    ///   씬을 넘어가면 로비 오브젝트는 사라진다. 그런데 게임 씬의
    ///   LanBotSpawner(봇 수)와 LanGameFlow(시작 인원)가 이 값을 알아야 한다.
    ///   DontDestroyOnLoad 오브젝트를 하나 더 만드는 것보다,
    ///   값만 담은 static이 훨씬 단순하고 추적하기 쉽다.
    ///
    /// ★ 참가자는 어떻게 아는가
    ///   몰라도 된다. 봇은 호스트만 스폰하고 시작 판정도 호스트만 한다.
    ///   참가자에게 필요한 건 모드 하나뿐이고, 그건 씬 로드 지시에 함께 온다.
    /// </summary>
    public static class LanRoomConfig
    {
        /// <summary>로비를 거쳐 왔는가. false면 씬을 직접 연 테스트다.</summary>
        public static bool HasValue { get; private set; }

        public static GameModeType Mode { get; private set; } = GameModeType.Absorb;

        /// <summary>사람 + AI를 합친 총 참가자 수.</summary>
        public static int TotalPlayers { get; private set; } = 4;

        /// <summary>그중 AI가 몇인지.</summary>
        public static int AiCount { get; private set; } = 2;

        /// <summary>모여야 하는 사람 수. 총원에서 AI를 뺀 값이며 최소 1명(방장)이다.</summary>
        public static int HumanCount
        {
            get { return Mathf.Max(1, TotalPlayers - AiCount); }
        }

        /// <summary>내 닉네임. 접속 후 호스트에게 알린다.</summary>
        public static string Nickname = "";

        public static void Set(GameModeType mode, int totalPlayers, int aiCount)
        {
            Mode = mode;
            TotalPlayers = Mathf.Max(1, totalPlayers);
            AiCount = Mathf.Clamp(aiCount, 0, TotalPlayers - 1);   // 사람이 최소 하나는 있어야 한다
            HasValue = true;
        }

        public static void Clear()
        {
            HasValue = false;
            Nickname = "";
        }
    }
}
