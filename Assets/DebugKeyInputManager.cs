using UnityEngine;
using Photon.Pun;

public class DebugKeyInputManager : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            PhotonNetwork.LoadLevel(GameModeManager.RESULT_SCENE_NAME);
        }
    }
}
