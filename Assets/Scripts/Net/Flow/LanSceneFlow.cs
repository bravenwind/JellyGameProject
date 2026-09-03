using UnityEngine;

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

            GameState.CurrentGameMode = mode;

            Begin(gameScene);
        }

        public static void ToResult(string resultScene)
        {
            if (string.IsNullOrEmpty(resultScene))
                return;

            Disconnect();

            Begin(resultScene);
        }

        public static void ToMain()
        {
            Disconnect();

            LanScoreboard.Clear();
            LanRoomConfig.Clear();

            Begin(MAIN_SCENE);
        }

        private static void Begin(string targetScene)
        {
            if (LoadingSceneController.IsTransitioning)
                return;

            LoadingSceneController.NextSceneName = targetScene;

            // ★ 여기서 Time.timeScale = 1 을 하면 안 된다
            //   종료 연출이 timeScale을 0으로 만들어 화면을 멈춘 상태로 넘어오는데,
            //   커튼이 화면을 덮기도 전에 여기서 풀어버리면 <b>슬로우가 먼저 풀린 게</b>
            //   그대로 보인다(멈춰 있던 캐릭터들이 갑자기 정상 속도로 튀는 그림).
            //   커튼 애니는 unscaled로 돌아 timeScale이 0이어도 정상 재생되므로,
            //   해제는 커튼이 화면을 다 덮은 순간(LoadingSceneController.OnDepartureSlideInDone)에 한다.
            //  ★ 예전엔 여기에 '커튼 없는 폴백'이 있었다
            //    TryBeginDepartureIntro가 false를 주면 timeScale을 풀고 곧장 Loading 씬으로
            //    넘어갔다. 그런데 false가 되는 조건이 둘 다 성립하지 않는다 — 중복 호출은
            //    바로 위 IsTransitioning이 막고, 커튼 프리팹은 항상 있다.
            LoadingSceneController.BeginDepartureIntro();
        }

        private static void Disconnect()
        {
            if (NetManager.Instance != null)
                NetManager.Instance.Shutdown();
        }
    }
}
