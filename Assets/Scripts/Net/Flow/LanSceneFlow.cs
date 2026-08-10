using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyNet
{
    public static class LanSceneFlow
    {
        public const string MAIN_SCENE = "Main";
        public const string LOADING_SCENE = "Loading";

        public static void ToGame(string gameScene, GameModeType mode)
        {
            if (string.IsNullOrEmpty(gameScene))
                return;

            LanLobby.SetChosenMode(mode);
            GameState.CurrentGameMode = mode;

            Begin(gameScene);
        }

        public static void ToResult(string resultScene)
        {
            if (string.IsNullOrEmpty(resultScene))
                return;

            Disconnect();
            PlayerMovement.InputLocked = false;

            Begin(resultScene);
        }

        //LAN은 서로 약속하고 모인 자리라 판이 도는 중에 빠지는 길을 두지 않는다
        //호스트가 사라져 판 자체가 깨진 경우에만 열어준다
        public static bool CanLeaveMatch
        {
            get
            {
                NetManager net = NetManager.Instance;

                if (net == null || net.CurrentMode == NetManager.Mode.None)
                    return true;

                if (net.ConnectionLost)
                    return true;

                LanGameFlow flow = LanGameFlow.Instance;

                return flow == null || flow.Phase == GamePhase.GameOver;
            }
        }

        public static void ToMain()
        {
            Disconnect();

            LanScoreboard.Clear();
            LanRoomConfig.Clear();
            LanLobby.ClearChosenMode();

            PlayerMovement.InputLocked = false;
            Time.timeScale = 1f;

            string cur = SceneManager.GetActiveScene().name;
            if (cur == MAIN_SCENE || cur == LOADING_SCENE)
            {
                SceneManager.LoadScene(MAIN_SCENE);
                return;
            }

            Begin(MAIN_SCENE);
        }

        private static void Begin(string targetScene)
        {
            if (LoadingSceneController.IsTransitioning)
                return;

            Time.timeScale = 1f;

            LoadingSceneController.NextSceneName = targetScene;
            LoadingSceneController.LocalLoad = true;
            LoadingSceneController.AllClientsLoad = false;

            if (LoadingSceneController.TryBeginDepartureIntro())
                return;

            SceneManager.LoadScene(LOADING_SCENE);
        }

        private static void Disconnect()
        {
            if (NetManager.Instance != null)
                NetManager.Instance.Shutdown();
        }
    }
}
