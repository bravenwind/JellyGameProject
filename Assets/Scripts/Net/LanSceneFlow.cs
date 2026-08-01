using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyNet
{
    /// <summary>
    /// 씬 전환 창구. Main · 게임 · 결과 사이의 이동을 전부 여기로 모은다.
    ///
    /// ═══════════════════════════════════════════════════════
    ///  ★ 왜 한 곳으로 모으는가
    /// ═══════════════════════════════════════════════════════
    ///
    ///  전환 하나마다 챙길 게 셋인데, 부르는 곳마다 조금씩 빠뜨렸다.
    ///
    ///    ① 커튼을 <b>출발 씬에서</b> 먼저 띄우기
    ///    ② 소켓을 끊을지 유지할지
    ///    ③ 다음 판을 위해 남은 상태 지우기
    ///
    ///  ①을 빠뜨리면 무거운 씬 언로드 위에서 애니메이션이 돌아 끊긴다.
    ///  ②를 빠뜨리면 결과 화면에서 아무도 안 읽는 메시지가 계속 쌓인다.
    ///  ③을 빠뜨리면 다음 판에 지난 판의 순위·모드가 새어 들어온다.
    ///
    /// ═══════════════════════════════════════════════════════
    ///  ★ 커튼을 출발 씬에서 띄운다는 것
    /// ═══════════════════════════════════════════════════════
    ///
    ///  Loading 씬을 바로 로드하면, 커튼이 그 씬에서 처음 생성되며 슬라이드인을
    ///  시작하는데 <b>그 순간이 출발 씬 언로드와 겹친다.</b>
    ///  맵 타일과 젤리 수백 개가 해제되는 프레임 위에서 애니메이션이 도니 끊긴다.
    ///
    ///  TryBeginDepartureIntro는 커튼을 출발 씬에 먼저 띄워 화면을 덮고,
    ///  덮은 뒤에 Loading 씬 로드를 스스로 주도한다.
    ///  무거운 언로드는 커튼이 정지해 있는 동안 지나가므로 눈에 띄지 않는다.
    ///
    ///  (Resources/LoadingCurtain 프리팹이 있어야 한다. 없으면 폴백으로 그냥 로드)
    /// </summary>
    public static class LanSceneFlow
    {
        public const string MainScene = "Main";
        public const string LoadingScene = "Loading";

        // ═════════════════════════════════════════════
        //  Main → 게임
        // ═════════════════════════════════════════════
        //
        // 소켓은 <b>유지한다.</b> 로비에서 맺은 연결 그대로 게임에 들어간다.
        public static void ToGame(string gameScene, GameModeType mode)
        {
            if (string.IsNullOrEmpty(gameScene)) return;

            // 씬이 로드되기 전에 정해둬야 한다 — 씬 안의 스크립트들이
            // Awake/Start에서 GameState.CurrentGameMode를 읽는다.
            LanLobby.SetChosenMode(mode);
            GameState.CurrentGameMode = mode;

            Begin(gameScene);
        }

        // ═════════════════════════════════════════════
        //  게임 → 결과
        // ═════════════════════════════════════════════
        //
        // 소켓을 <b>끊는다.</b> 순위는 이미 LanScoreboard에 스냅샷으로 담아 뒀고,
        // 결과 씬에는 메시지를 처리할 사람이 없다.
        public static void ToResult(string resultScene)
        {
            if (string.IsNullOrEmpty(resultScene)) return;

            Disconnect();
            PlayerMovement.InputLocked = false;

            Begin(resultScene);
        }

        // ═════════════════════════════════════════════
        //  게임 → Main  /  결과 → Main
        // ═════════════════════════════════════════════
        //
        // 소켓을 끊고, 다음 판을 위해 상태를 전부 비운다.
        public static void ToMain()
        {
            Disconnect();

            // ★ 다음 판에 지난 판이 새어 들어오지 않게.
            //   특히 ChosenMode가 남아 있으면 LanGameFlow가 "씬 설정과 모드가 다르다"고
            //   경고하며 지난 판의 모드를 덮어쓴다.
            LanScoreboard.Clear();
            LanRoomConfig.Clear();
            LanLobby.ClearChosenMode();

            PlayerMovement.InputLocked = false;
            Time.timeScale = 1f;

            // 이미 Main이면(또는 커튼이 없는 로딩 씬이면) 커튼을 또 띄우지 않는다
            string cur = SceneManager.GetActiveScene().name;
            if (cur == MainScene || cur == LoadingScene)
            {
                SceneManager.LoadScene(MainScene);
                return;
            }

            Begin(MainScene);
        }

        // ═════════════════════════════════════════════
        //  공통
        // ═════════════════════════════════════════════
        static void Begin(string targetScene)
        {
            // ★ 이미 전환이 진행 중이면 아무것도 하지 않는다.
            //
            //   TryBeginDepartureIntro는 두 가지 이유로 false를 돌려준다.
            //     ① 커튼 프리팹이 없다        → 폴백으로 그냥 로드하는 게 맞다
            //     ② 이미 커튼이 떠 있다        → 로드하면 <b>진행 중인 전환을 덮어쓴다</b>
            //
            //   반환값만으로는 둘을 구분할 수 없어서, ②를 여기서 먼저 걸러낸다.
            //   실제로 일어나는 상황이다 — "메인으로"를 두 번 누르거나,
            //   게임 종료 자동 전환이 도는 도중에 버튼을 누르는 경우.
            if (LoadingSceneController.IsTransitioning) return;

            LoadingSceneController.NextSceneName = targetScene;
            LoadingSceneController.LocalLoad = true;    // LAN에는 PUN LoadLevel이 없다 — 각자 로드
            LoadingSceneController.AllClientsLoad = false;

            // 커튼이 출발 씬에서 먼저 뜨고, 이후 Loading → 목적지를 스스로 주도한다.
            if (LoadingSceneController.TryBeginDepartureIntro()) return;

            // 폴백: 커튼 프리팹이 없을 때만 (끊김이 보일 수 있다)
            SceneManager.LoadScene(LoadingScene);
        }

        static void Disconnect()
        {
            if (NetManager.Instance != null) NetManager.Instance.Shutdown();
        }
    }
}
