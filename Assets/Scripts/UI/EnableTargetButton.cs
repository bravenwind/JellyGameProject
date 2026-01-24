using UnityEngine;

public class EnableTargetButton : MonoBehaviour
{
    [SerializeField] private GameObject target;

    public void EnableTarget()
    {
        if (target != null)
            target.SetActive(true);
    }
}
