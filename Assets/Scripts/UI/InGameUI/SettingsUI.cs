using UnityEngine;

public class SettingsUI : MonoBehaviour
{
    [SerializeField] private ButtonScaleUp buttonScaleUp;

    public void ReturnToInGame()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.SetState(UIState.InGame);

        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.PlayButton1Sound();

        if (buttonScaleUp != null)
            buttonScaleUp.OnPointerExit();
    }
}
