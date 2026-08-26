using UnityEngine;
using JellyNet;

//결과 씬을 바로 띄워보는 디버그 키
public class DebugKeyInputManager : MonoBehaviour
{
    [SerializeField] private KeyCode changeToResultSceneKey = KeyCode.F1;

    private void Update()
    {
        if (!Input.GetKeyDown(changeToResultSceneKey))
            return;

        LanGameFlow flow = LanGameFlow.Instance;

        if (flow == null)
            return;

        //모드에 따라 어느 결과 씬인지는 LanGameFlow가 안다 — 여기서 다시 갈라지지 않는다
        LanSceneFlow.ToResult(flow.ResultSceneName);
    }
}
