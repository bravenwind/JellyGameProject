using UnityEngine;

public class CylinderAbsorbField : MonoBehaviour
{
    [SerializeField]
    private Transform meshTransform;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Edible"))
        {
            Debug.Log("Á©¸® °¨ÁöµÊ.");
            JellyAbsorbEffect jelly = other.GetComponent<JellyAbsorbEffect>();
            if (jelly != null)
            {
                jelly.SetTarget(meshTransform);
            }
            other.attachedRigidbody.useGravity = false;
            other.isTrigger = true;
        }
    }
}
