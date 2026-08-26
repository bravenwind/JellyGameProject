using System;
using UnityEngine;

//Countdown은 '판이 곧 시작한다'는 상태다. 예전엔 이 상태를 표현할 자리가 없어서
//LanGameFlow가 Phase=Loading + countdownRunning 플래그로 나눠 들고 있었고,
//클라에게 알리려고 MsgType.CountdownStart라는 메시지까지 따로 있었다.
//단계에 이름을 주면 셋이 하나가 된다
public enum GamePhase { None, Loading, Countdown, Playing, GameOver, Result }

public enum GameModeType { Absorb, Push }

//내 화면 하나에만 해당하는 전역 상태. 네트워크로 오가는 값은 여기 두지 않는다
//(그건 LanPlayerState·LanGameFlow가 들고, 호스트가 권위를 가진다)
public static class GameState
{
    // ── Events ──
    //구독자가 있는 것만 남긴다. OnPhaseChanged·OnScoreChanged는 구독자가 하나도 없어
    //Invoke 비용만 내고 있었다 — 단계 변화는 LanGameFlow.SetPhaseLocal이,
    //점수 표시는 PlayerEvents.OnScaleUIUpdate가 이미 담당한다
    public static event Action<float> OnScaleChanged;
    public static event Action<Color> OnDisplayColorChanged;

    // ── Backing fields ──
    private static float playerCurrentScale = 2f;
    private static Color currentDisplayColor = Color.white;

    // ── Properties & Event invoking ──
    public static GamePhase Phase { get; set; } = GamePhase.None;

    public static int CurrentScore { get; set; }

    public static float PlayerCurrentScale
    {
        get => playerCurrentScale;
        set
        {
            if (Mathf.Approximately(playerCurrentScale, value))
                return;
            playerCurrentScale = value;
            OnScaleChanged?.Invoke(playerCurrentScale);
        }
    }

    public static Color CurrentDisplayColor
    {
        get => currentDisplayColor;
        set
        {
            currentDisplayColor = value;
            OnDisplayColorChanged?.Invoke(currentDisplayColor);
        }
    }

    public static GameModeType CurrentGameMode { get; set; } = GameModeType.Absorb;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        Phase = GamePhase.None;
        CurrentScore = 0;
        playerCurrentScale = 2f;
        currentDisplayColor = Color.white;
        CurrentGameMode = GameModeType.Absorb;

        OnScaleChanged = null;
        OnDisplayColorChanged = null;
    }

    public static void ResetValues()
    {
        Phase = GamePhase.None;
        CurrentScore = 0;
        playerCurrentScale = 2f;
        currentDisplayColor = Color.white;

        // [H1] 여기서 이벤트를 null로 비우면 안 된다.
        // 씬 UI(CurrentStatusUI 등)는 OnEnable(씬 활성화)에서 구독하는데, 게임 시작이
        // 이 함수를 그 뒤에 호출하므로 구독이 통째로 끊겨 UI 갱신이 멈춘다.
        // 구독 해제는 각 구독자의 OnDisable 책임이고, 전체 정리는 도메인 리로드 대비용인
        // Reset()(SubsystemRegistration)에서만 수행한다.
    }
}
