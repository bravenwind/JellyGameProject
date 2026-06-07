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
            switch (GameState.CurrentGameMode)
            {
                case GameModeType.Absorb:
                    PhotonNetwork.LoadLevel(GameModeManager.RESULT_SCENE_NAME_ABSORB);
                    break;
                case GameModeType.Push:
                    PhotonNetwork.LoadLevel(GameModeManager.RESULT_SCENE_NAME_PUSH);
                    break;
            }
            ;
        }
    }
}
