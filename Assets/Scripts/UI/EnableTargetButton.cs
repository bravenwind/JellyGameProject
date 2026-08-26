using UnityEngine;

public class EnableTargetButton : MonoBehaviour
{
    public GameObject target;

    public void EnableTarget()
    {
        if (target != null)
        {
            PlaySFXAudio.Instance.PlayButton1Sound();
            target.SetActive(true);
        }
    }
}
