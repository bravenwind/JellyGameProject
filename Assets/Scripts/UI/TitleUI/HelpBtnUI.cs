using UnityEngine;

public class HelpBtnUI : MonoBehaviour
{
    public GameObject helpBtnPanel;

    public void SetHelpBtnPanel()
    {
        helpBtnPanel.SetActive(!helpBtnPanel.activeSelf);
    }
}
