using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public LoadingBGSlideAni loadingBGSlideAni;

    public void LoadGame()
    {
        if (SceneManager.GetActiveScene() == SceneManager.GetSceneByName("Main"))
        {
            loadingBGSlideAni.LoadSceneWithSlide("Game");
        }
        else
        {
            SceneManager.LoadScene("Game");
        }
        //SceneManager.LoadScene("Game");
    }

    public void LoadMain()
    {
        SceneManager.LoadScene("Main");
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
