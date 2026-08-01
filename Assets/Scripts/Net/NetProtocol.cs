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

        // 기존 게임 시스템(PlayerScaleController·PlayerColorVisual)을 그대로 쓰기 위한 이벤트.
        // 절대값을 맞추는 대신 "무슨 일이 있었는지"를 알려 각 클라가 같은 함수를 부르게 한다.
        GrowEvent = 26,         // 호스트 → 전체 : netId, 종류, 값
        AnimState = 27,         // 소유자 → 호스트 → 나머지 : netId, 애니메이터 상태 해시

        EatJellyRequest = 30,  // 클라 → 호스트 : "이 젤리 먹었다" 요청(판정은 호스트가)
        EatJellyConfirm = 31,  // 호스트 → 전체 : "누가 이겼고 보상은 이것" 확정

        AbsorbPlayerRequest = 32, // 클라 → 호스트 : "이 플레이어를 흡수했다" 주장
        PlayerAbsorbed = 33,      // 호스트 → 전체 : 흡수 확정(연출 시작)
        PlayerRespawn = 34,       // 호스트 → 전체 : 부활 위치
        EliminateRequest = 35,    // 클라 → 호스트 : "나 초콜릿에 빠졌다" (결과는 PlayerStateUpdate)
        SetMyName = 36,           // 클라 → 호스트 : 내 닉네임 (호스트가 PlayerNameSet으로 재방송)

        // ── 5단계: 밀치기 모드 ──
        BatHitRequest = 40,    // 클라 → 호스트 : "이 상대를 때렸다" 주장(판정은 호스트)
        Knockback = 41,        // 호스트 → 피격자 소유자에게만 : 밀려나라

        // ── 게임 흐름 ──
        GamePhaseChange = 50,  // 호스트 → 전체 : 단계 전환 (+ 모드, 남은 시간)
        GameOver = 51,         // 호스트 → 전체 : 승자 확정
        FinalStandings = 52,   // 호스트 → 전체 : 최종 순위 (결과 씬으로 들고 간다)
        LoadGameScene = 53,    // 호스트 → 전체 : "게임 씬으로 들어와라" (로비 → 게임)
        SceneReady = 54,       // 클라 → 호스트 : "게임 씬 준비 끝났다. 내 캐릭터를 만들어달라"
        CountdownStart = 55,   // 호스트 → 전체 : "지금부터 3·2·1 세라" (다 같이 시작하기 위함)

        // ── 맵 ──
        TileCollapse = 60,     // 호스트 → 전체 : 밟아서 무너진 타일 좌표
        TileWear = 61,         // 호스트 → 전체 : 타일이 밟힌 횟수(어두워지는 단계)

        // ── AI 봇 ──
        //
        // ★ 봇은 호스트가 전부 굴린다(원본의 MasterClient 권위를 그대로 옮긴 것).
        //   위치·회전은 NetTransform이, 애니메이션은 AnimState가 이미 담당하므로
        //   봇 전용으로 남는 건 '크기'와 '탈락' 둘뿐이다.
        // 크기와 색을 한 메시지로 묶는다 — 둘 다 '젤리를 먹었을 때' 함께 바뀐다.
        // (봇은 호스트에서만 젤리를 먹으므로 클라는 결과를 받는 수밖에 없다)
        BotState = 70,         // 호스트 → 전체 : netId, 크기, r, g, b
        BotEliminated = 71,    // 호스트 → 전체 : netId (초콜릿·낙사 등으로 탈락)
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

        /// <summary>
        /// 씬에 배치된 오브젝트의 ID는 이 값부터 시작한다.
        /// 런타임 스폰 ID(1부터 증가)와 절대 겹치지 않게 하기 위함.
        /// </summary>
        public const int SceneIdBase = 1000000;
    }

    /// <summary>성장의 종류. 기존 PlayerScaleController의 진입점과 1:1 대응한다.</summary>
    public enum GrowKind : byte
    {
        Jelly = 0,      // GrowByJelly()        — 젤리를 먹음
        Absorbing = 1,  // GrowByAbsorbing(v)   — 플레이어를 흡수 (v = 상대 크기)
        BatHit = 2      // GrowByBatHit(g)      — 배트로 때림 (g = 성장량)
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
