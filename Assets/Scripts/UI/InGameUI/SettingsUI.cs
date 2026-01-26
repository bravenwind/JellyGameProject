using System.Xml.Serialization;
using UnityEngine;

public class SettingsUI : MonoBehaviour
{
    public ButtonScaleUp buttonScaleUp;

    public void ReturnToInGame()
    {
        UIManager.Instance.SetState(UIState.InGame);
        PlaySFXAudio.Instance.PlayButton1Sound();
        buttonScaleUp.OnPointerExit();
    }
}
