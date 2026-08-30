using UnityEngine;

public class DisableSelfButton : MonoBehaviour
{
    public void DisableSelf()
    {
        PlaySFXAudio.Instance.PlayButton1Sound();
        gameObject.SetActive(false);
    }
}
