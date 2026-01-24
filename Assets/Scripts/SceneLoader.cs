using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{ 
        // [수정] Scene 대신 string 사용
    public string gameSceneName = "LevelDesign";
    public string titleSceneName = "Main";

    // Start 버튼에 연결
    public void LoadGame()
    {
        PlaySFXAudio.Instance.PlayButtonClickSound();
        //SceneManager.LoadScene(gameScene.name);
        SceneManager.LoadScene(gameSceneName);
    }

    // 메인으로 돌아가기 버튼에 연결
    public void LoadMain()
    {
        PlaySFXAudio.Instance.PlayButtonClickSound();
        //SceneManager.LoadScene(titleScene.name);
        SceneManager.LoadScene(titleSceneName);
    }

    // Exit 버튼에 연결(에디터에선 종료 안 됨)
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
