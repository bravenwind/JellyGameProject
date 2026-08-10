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
