using System;
using UnityEngine;

public class PlayerAbsorber : MonoBehaviour
{
    public Action<JellyColorType> OnJellyEaten;
    public Action OnJellyScored;
    public Action OnResetRequested;

    [Header("Detection Settings")]
    public Transform detectTransform;
    public float playerBaseHeight = 1.5f;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Edible"))
        {
            JellyColliderAbsorb jca = other.GetComponentInParent<JellyColliderAbsorb>();
            if (jca != null) jca.StartAbsorb(transform);

            Rigidbody rb = other.GetComponentInParent<Rigidbody>();
            if (rb != null) rb.constraints = RigidbodyConstraints.None;
            other.isTrigger = true;
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        OnJellyScored?.Invoke();
        OnJellyEaten?.Invoke(type);
    }
}
