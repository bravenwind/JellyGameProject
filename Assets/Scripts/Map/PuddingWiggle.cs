using UnityEngine;

public class PuddingWiggle : MonoBehaviour
{
    public Animator puddingAnimator;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("PlayerMesh"))
        {
            puddingAnimator.SetTrigger("Wiggle");
        }
    }
}
