using UnityEngine;
using JellyNet;

/// <summary>
/// 씬 밖으로 나가는 버튼들의 핸들러. 타이틀(Main)과 결과 씬에 하나씩 놓여 있다.
///
/// ★ LoadGame()은 지웠다 — 도달하지 않는 코드였다
///   SceneManager.LoadScene("Game")을 불렀는데 <b>"Game"이라는 씬은 존재하지 않는다.</b>
///   빌드 목록은 Main / Loading / Game_io_AbsorbMode / Game_io_PushMode /
///   GameResult_AbsorbMode / GameResult_PushMode 여섯 개다. 눌렸다면 예외가 났을 텐데,
///   다섯 씬을 뒤져도 이 메서드에 연결된 버튼이 하나도 없었다. 방 만들기·참가는
///   전부 LanSceneFlow가 맡은 지 오래다.
///
/// ★ ReturnToMain()은 UIManager에서 옮겨 왔다
///   결과 씬의 MainMenuButton이 UIManager.OnClick_MainMenuButton을 부르고 있었는데,
///   UIManager는 같은 오브젝트에 이 스크립트와 나란히 붙어 있으면서
///   정작 자기 본업(UIState별 UI 켜고 끄기)은 uiList가 비어 있어 하지 않고 있었다.
///   같은 오브젝트의 "버튼 누르면 씬을 뜬다"는 일은 여기 하나로 모은다.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    /// <summary>"메인으로" 버튼. 소켓 정리와 씬 전환은 LanSceneFlow가 맡는다.</summary>
    public void ReturnToMain()
    {
        PlaySFXAudio.Instance.PlayButton1Sound();
        LanSceneFlow.ToMain();
    }

    /// <summary>"게임 종료" 버튼.</summary>
    public void QuitGame()
    {
        PlaySFXAudio.Instance.PlayButton1Sound();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
