using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// 화면에 어떤 UI 묶음을 띄울지 정하는 상태.
///
/// ★ 값을 지우거나 순서를 바꾸면 안 된다
///   씬의 uiList는 이 enum을 <b>정수로</b> 저장한다. 중간 값을 지우면 뒤가 한 칸씩
///   밀려서, 인스펙터에서 InGame으로 맞춰둔 항목이 조용히 Settings가 된다.
///   지금 Menu·Help는 아무도 쓰지 않지만 그 이유로 남겨둔다.
/// </summary>
public enum UIState
{
    None,       // 아무것도 띄우지 않음
    Pause,
    Settings,   // 설정 창
    InGame,     // 게임 플레이 중 HUD
    GameOver,
    Menu,
    Help
}

/// <summary>
/// 인게임 UI 묶음 전환 담당.
///
/// ★ 이 클래스가 하지 않는 일
///   - 게임 진행(카운트다운·종료 연출)은 LanGameFlow가 한다
///   - 씬 전환과 소켓 정리는 LanSceneFlow가 한다
///   예전엔 여기가 Time.timeScale과 씬 전환까지 건드려서 그 둘과 소유권을 다퉜다.
///
/// ★ 곧 사라질 클래스다 — 새 코드가 여기에 기대지 말 것
///   씬 네 곳(Game_io×2, GameResult×2)에 붙어 있지만 <b>uiList가 전부 비어 있다.</b>
///   즉 SetState는 아무 오브젝트도 켜고 끄지 않고, Pause 상태로 들어가는 곳이 없어
///   ApplyPause도 늘 timeScale=1을 쓸 뿐이며, ESC 토글도 보이는 변화가 없다.
///   실제로 일을 하던 둘은 각자의 자리로 옮겼다:
///     · 페이드 인      → ScreenFader (FadeImage 오브젝트가 직접 갖는다)
///     · "메인으로" 버튼 → SceneLoader (같은 오브젝트에 이미 있던 버튼 핸들러)
///   남은 SetState/UIState를 부르는 곳은 인게임 설정 UI 세 개뿐이고,
///   그 UI는 걷어내기로 했다. 씬에서 설정 오브젝트가 빠지면 이 파일과
///   SettingsUI·SoundSettings·TopRightButtonUI를 함께 지운다.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    /// <summary>인스펙터에서 상태와 UI 오브젝트를 짝지어 두는 항목.</summary>
    [Serializable]
    public class UIStateMapping
    {
        public UIState state;       // 어떤 상태일 때
        public GameObject uiObject; // 어떤 UI를 켤 것인가
    }

    [Header("UI 목록 (상태 ↔ 오브젝트)")]
    [SerializeField] private List<UIStateMapping> uiList = new List<UIStateMapping>();

    [Header("시작 상태")]
    [SerializeField] private UIState startState = UIState.InGame;

    public UIState CurrentState { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        SetState(startState);
    }

    // ─────────────────────────────────────────────────────────
    //  상태 전환
    // ─────────────────────────────────────────────────────────

    public void SetState(UIState newState)
    {
        //같은 상태로 다시 들어오면 아무 일도 하지 않는다.
        //예전엔 매번 전체 목록을 다시 껐다 켜서, 열려 있던 창이 깜빡였다
        if (CurrentState == newState)
            return;

        CurrentState = newState;

        for (int i = 0; i < uiList.Count; i++)
        {
            UIStateMapping m = uiList[i];
            if (m != null && m.uiObject != null)
                m.uiObject.SetActive(m.state == newState);
        }

        ApplyPause(newState == UIState.Pause);
    }

    // ★ 시간 정지는 Pause 하나에만 건다
    //
    //   예전엔 GameOver에서도 Time.timeScale = 0을 걸었다. 싱글 게임의 잔재인데,
    //   지금은 내가 탈락해도 <b>다른 사람들의 판은 계속 돌아간다.</b> 화면을 얼려버리면
    //   관전 카메라도 멈춰 아무것도 못 보게 된다.
    //   게임 종료 시점의 슬로우모션·정지는 LanGameFlow의 종료 연출이 소유한다 —
    //   여기서 같이 건드리면 둘이 서로의 값을 덮어쓴다.
    private void ApplyPause(bool paused)
    {
        Time.timeScale = paused ? 0f : 1f;
    }

    private void Update()
    {
        //ESC로 설정 창을 여닫는다. 상태마다 switch로 나누던 것을 토글 하나로 줄였다 —
        //분기가 늘어날 때마다 이 함수가 커지는 구조였고, 실제로 갈라진 건 이 한 가지뿐이었다
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        if (CurrentState == UIState.InGame)
            SetState(UIState.Settings);
        else if (CurrentState == UIState.Settings)
            SetState(UIState.InGame);
    }

}
