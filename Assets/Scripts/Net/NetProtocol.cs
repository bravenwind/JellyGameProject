// ============================================================
//  NetProtocol — 소켓 프로토콜의 공통 약속
//
//  패킷 형식:  [길이 4바이트][타입 1바이트][데이터 ...]
//
//  · 길이(4바이트)는 '타입 + 데이터'의 바이트 수. 프레이밍용.
//  · 타입(1바이트)은 아래 MsgType. 문자열 비교 대신 숫자 하나로 구분한다
//    (빠르고, 용량이 작고, 무엇보다 string 할당이 없어 GC 압박이 없다).
// ============================================================

namespace JellyNet
{
    /// <summary>메시지 종류. 값은 바꾸지 말 것(양쪽이 같은 숫자를 알아야 한다).</summary>
    public enum MsgType : byte
    {
        // ── 2단계: 연결 관리만 ──
        Welcome = 1,        // 호스트 → 새 클라 : "너의 ID는 N이다"
        PlayerJoined = 2,   // 호스트 → 전원   : 누가 들어왔다
        PlayerLeft = 3,     // 호스트 → 전원   : 누가 나갔다
        Ping = 4,           // 클라 → 호스트   : 왕복 시간 측정용
        Pong = 5,           // 호스트 → 클라   : Ping 되돌려주기
        Chat = 6,           // 양방향          : 연결 확인용 문자열

        // ── 3단계: 오브젝트 복제와 위치 동기화 ──
        SpawnEntity = 20,     // 호스트 → 전체 : 네트워크 오브젝트 생성
        DespawnEntity = 21,   // 호스트 → 전체 : 파괴
        TransformUpdate = 22, // 소유자 → 호스트 → 나머지 : 위치·회전

        // ── 4단계: 흡수 모드 ──
        StateUpdate = 23,      // 호스트 → 전체 : 크기(스케일)

        // 점수·탈락·색을 한 메시지로 묶는다. 셋 다 자주 안 바뀌고 대개 함께 바뀐다.
        PlayerStateUpdate = 24, // 호스트 → 전체 : netId, score, flags, r, g, b
        PlayerNameSet = 25,     // 호스트 → 전체 : netId, 이름

        EatJellyRequest = 30,  // 클라 → 호스트 : "이 젤리 먹었다" 요청(판정은 호스트가)
        EatJellyConfirm = 31,  // 호스트 → 전체 : "누가 이겼고 보상은 이것" 확정

        AbsorbPlayerRequest = 32, // 클라 → 호스트 : "이 플레이어를 흡수했다" 주장
        PlayerAbsorbed = 33,      // 호스트 → 전체 : 흡수 확정(연출 시작)
        PlayerRespawn = 34,       // 호스트 → 전체 : 부활 위치

        // ── 5단계: 밀치기 모드 ──
        BatHitRequest = 40,    // 클라 → 호스트 : "이 상대를 때렸다" 주장(판정은 호스트)
        Knockback = 41,        // 호스트 → 피격자 소유자에게만 : 밀려나라

        // ── 게임 흐름 ──
        GamePhaseChange = 50,  // 호스트 → 전체 : 단계 전환 (+ 모드, 남은 시간)
        GameOver = 51,         // 호스트 → 전체 : 승자 확정

        // ── 이후 추가 예정 ──
        // TileCollapse   = 60,   // 타일 붕괴
    }

    public static class NetConfig
    {
        /// <summary>기본 포트. 콘솔 실습과 겹치지 않게 7777을 쓴다.</summary>
        public const int DefaultPort = 7777;

        /// <summary>이보다 큰 메시지가 오면 깨진 것으로 보고 연결을 끊는다.</summary>
        public const int MaxBodySize = 64 * 1024;

        /// <summary>수신 누적 버퍼 초기 크기. 부족하면 자동으로 늘어난다.</summary>
        public const int RecvBufferInitial = 8 * 1024;

        /// <summary>위치를 초당 몇 번 보낼지. 20Hz = 50ms 간격.</summary>
        public const float TransformSendRate = 20f;

        /// <summary>프리팹 등록표에서 젤리가 시작되는 인덱스. 0번은 플레이어.</summary>
        public const int JellyPrefabStart = 1;
    }

    /// <summary>플레이어 상태 비트. 1바이트에 담아 보낸다.</summary>
    [System.Flags]
    public enum PlayerFlags : byte
    {
        None = 0,
        Eliminated = 1 << 0,   // 맵 밖으로 탈락
        Absorbed = 1 << 1,     // 흡수되어 리스폰 대기 중
    }
}
