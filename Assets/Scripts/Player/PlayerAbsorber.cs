using System;
using UnityEngine;

public class PlayerAbsorber : MonoBehaviour
{
    public Action<JellyColorType> OnJellyEaten;
    public Action OnResetRequested;

    [Header("Detection Settings")]
    public Transform detectTransform;
    public float playerBaseHeight = 1.5f;

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.gameObject.CompareTag("Edible"))
        {
            JellyColliderAbsorb jca = hit.gameObject.GetComponentInParent<JellyColliderAbsorb>();
            if (jca != null) jca.StartAbsorb(transform);

            Rigidbody rb = hit.gameObject.GetComponentInParent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.None;
            hit.collider.isTrigger = true;
        }
    }

    public void AbsorbColor(JellyColorType type)
    {
        DataManager.Instance.absorbedJellyCount++;
        DataManager.Instance.currentScore += 100;

        UIPoolManager.Instance.SpawnUI(UIType.JellyEat);
        PlaySFXAudio.Instance.PlayColorMixSound();

        // PlayerManager에게 젤리를 먹었다고 보고
        OnJellyEaten?.Invoke(type);

        if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
        {
            DataManager.Instance.missions[1].missionCleared = true;
        }
    }
}