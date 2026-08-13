using System;

namespace JellyNet
{
    public enum MsgType : byte
    {
        Welcome = 1,
        PlayerJoined = 2,
        PlayerLeft = 3,
        Ping = 4,
        Pong = 5,
        Chat = 6,

        SpawnEntity = 20,
        DespawnEntity = 21,
        TransformUpdate = 22,
        StateUpdate = 23,

        PlayerStateUpdate = 24,
        PlayerNameSet = 25,

        GrowEvent = 26,
        AnimState = 27,

        EatJellyRequest = 30,
        EatJellyConfirm = 31,
        AbsorbPlayerRequest = 32,
        PlayerAbsorbed = 33,
        PlayerRespawn = 34,
        EliminateRequest = 35,
        SetMyName = 36,
        KilledBy = 37,

        BatHitRequest = 40,
        Knockback = 41,

        GamePhaseChange = 50,
        GameOver = 51,
        FinalStandings = 52,
        LoadGameScene = 53,
        SceneReady = 54,
        CountdownStart = 55,

        TileCollapse = 60,
        TileWear = 61,

        BotState = 70,
        BotEliminated = 71,
    }

    public static class NetConfig
    {
        public const int DEFAULT_PORT = 7777;

        public const int MAX_BODY_SIZE = 64 * 1024;
        public const int RECV_BUFFER_INITIAL = 8 * 1024;

        public const float TRANSFORM_SEND_RATE = 20f;

        public const int JELLY_PREFAB_START = 1;

        public const int SCENE_ID_BASE = 1000000;
    }

    public enum GrowKind : byte
    {
        Jelly = 0,
        Absorbing = 1,
        BatHit = 2
    }

    [Flags]
    public enum PlayerFlags : byte
    {
        None = 0,
        Eliminated = 1 << 0,
        Absorbed = 1 << 1,
    }
}
