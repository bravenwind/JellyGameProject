using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameQuit : MonoBehaviour
{
    public void QuitGame()
    {
        PlaySFXAudio.Instance.PlayButtonClickSound();
        Debug.Log("게임 종료");
        Application.Quit();
    }
}
