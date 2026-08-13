using UnityEngine;

//결과 씬을 바로 띄워보는 디버그 키
public class DebugKeyInputManager : MonoBehaviour
{
    [SerializeField]
    private KeyCode changeToResultSceneKey = KeyCode.F1;

    private void Update()
    {
        if (!Input.GetKeyDown(changeToResultSceneKey))
            return;

        JellyNet.LanGameFlow flow = JellyNet.LanGameFlow.Instance;

        if (flow == null)
            return;

        JellyNet.LanSceneFlow.ToResult(GameState.CurrentGameMode == GameModeType.Push
            ? flow.resultScenePush
            : flow.resultSceneAbsorb);
    }
}
