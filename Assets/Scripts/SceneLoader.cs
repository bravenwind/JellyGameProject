using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadGame()
    {
        PlaySFXAudio.Instance.PlayButton1Sound();
        SceneManager.LoadScene("Game");
    }

    public void LoadMain()
    {
        PlaySFXAudio.Instance.PlayButton1Sound();
        SceneManager.LoadScene("Main");
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        PlaySFXAudio.Instance.PlayButton1Sound();
        UnityEditor.EditorApplication.isPlaying = false;
#else
        PlaySFXAudio.Instance.PlayButton1Sound();
        Application.Quit();
#endif
    }
}
