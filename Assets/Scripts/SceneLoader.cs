using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    // Start 버튼에 연결
    public void LoadGame()
    {
        SceneManager.LoadScene("TileScene");
    }

    // 메인으로 돌아가기 버튼에 연결
    public void LoadMain()
    {
        SceneManager.LoadScene("Main");
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
