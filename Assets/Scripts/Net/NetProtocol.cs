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

        // ── 3단계 이후 추가 예정 ──
        // SpawnEntity    = 20,
        // DespawnEntity  = 21,
        // TransformUpdate= 22,
        // StateUpdate    = 23,
        // FullSnapshot   = 24,
    }

    public static class NetConfig
    {
        /// <summary>기본 포트. 콘솔 실습과 겹치지 않게 7777을 쓴다.</summary>
        public const int DefaultPort = 7777;

        /// <summary>이보다 큰 메시지가 오면 깨진 것으로 보고 연결을 끊는다.</summary>
        public const int MaxBodySize = 64 * 1024;

        /// <summary>수신 누적 버퍼 초기 크기. 부족하면 자동으로 늘어난다.</summary>
        public const int RecvBufferInitial = 8 * 1024;
    }
}
