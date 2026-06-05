using UnityEngine;
using Photon.Pun;

public class DebugKeyInputManager : MonoBehaviour
{
    [SerializeField]
    private KeyCode changeToResultSceneKey = KeyCode.F1;

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(changeToResultSceneKey))
        {
            PhotonNetwork.LoadLevel(GameModeManager.RESULT_SCENE_NAME);
        }
    }
}
