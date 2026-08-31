using System;
using UnityEngine;

//Countdown은 '판이 곧 시작한다'는 상태다. 예전엔 이 상태를 표현할 자리가 없어서
//LanGameFlow가 Phase=Loading + countdownRunning 플래그로 나눠 들고 있었고,
//클라에게 알리려고 MsgType.CountdownStart라는 메시지까지 따로 있었다.
//단계에 이름을 주면 셋이 하나가 된다
public enum GamePhase { None, Loading, Countdown, Playing, GameOver, Result }

public enum GameModeType { Absorb, Push }

// ═══════════════════════════════════════════════════════════════
//  내 화면 하나에만 해당하는 전역 상태
// ═══════════════════════════════════════════════════════════════
//
// ★ 여기 있는 값은 전부 <b>표시용</b>이다. 판정에 쓰면 안 된다
//   PlayerCurrentScale·CurrentScore는 내 HUD가 즉시 반응하려고 두는 <b>예측치</b>다.
//   실제 판정(누가 누구를 먹을 수 있나, 최종 순위)은 호스트가 확정한
//   LanPlayerState·NetEntity의 값으로만 한다.
//
//   경계가 흐려지면 "내 화면에선 먹었는데 서버는 아니라더라"가 생긴다.
//   판정용 크기를 찾는다면 NetEntity.ScaleOf가 유일한 창구다.
//
// ★ 여기 있는 값은 전부 <b>내 캐릭터 것</b>이다
//   원격 아바타의 값이 새어 들어오지 않게, PlayerBridge가 IsLocalOwner로 거른 뒤에만
//   쓴다. 이 클래스에는 소유권 개념이 없으므로 거르는 책임은 전적으로 부르는 쪽에 있다.
public static class GameState
{

    // ★ OnScaleChanged · OnDisplayColorChanged · CurrentDisplayColor를 지웠다
    //   유일한 구독자가 CurrentStatusUI였는데, 그 HUD는 두 게임 씬 모두
    //   비활성 오브젝트(CurrentJelly)에 붙어 있었고 켜는 코드가 어디에도 없었다.
    //   OnEnable이 돌지 않으니 구독조차 하지 않았고, 결국 두 이벤트는
    //   <b>아무도 듣지 않는데 계속 발화</b>하고 CurrentDisplayColor는 쓰기 전용이었다.
    //
    //   PlayerCurrentScale은 남는다 — PlayerBridge가 예측 점수를 계산할 때 읽는다.
    public static Action OnCameraScaleIncreased;

    private static float playerCurrentScale;

    // ── Properties & Event invoking ──
    public static GamePhase Phase { get; set; } = GamePhase.None;

    public static int CurrentScore { get; set; }

    public static float PlayerCurrentScale
    {
        get => playerCurrentScale;
        set => playerCurrentScale = value;
    }

    public static GameModeType CurrentGameMode { get; set; } = GameModeType.Absorb;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        Phase = GamePhase.None;
        CurrentScore = 0;
        playerCurrentScale = 0f;   //스폰된 캐릭터의 OnScaleSettled가 채운다
        CurrentGameMode = GameModeType.Absorb;

        OnCameraScaleIncreased = null;
    }

    public static void ResetValues()
    {
        Phase = GamePhase.None;
        CurrentScore = 0;
        playerCurrentScale = 0f;   //스폰된 캐릭터의 OnScaleSettled가 채운다

        // [H1] 여기서 이벤트를 null로 비우면 안 된다.
        // 씬 UI는 OnEnable(씬 활성화)에서 구독하는데, 게임 시작이 이 함수를 그 뒤에
        // 호출하므로 구독이 통째로 끊겨 갱신이 멈춘다.
        // 구독 해제는 각 구독자의 OnDisable 책임이고, 전체 정리는 도메인 리로드 대비용인
        // Reset()(SubsystemRegistration)에서만 수행한다.
    }
}
