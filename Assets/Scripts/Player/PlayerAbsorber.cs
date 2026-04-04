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

    public bool isBot = false;

    public void AbsorbColor(JellyColorType type)
    {
        if (!isBot)
        {
            DataManager.Instance.absorbedJellyCount++;
            DataManager.Instance.currentScore += 100;
            UIPoolManager.Instance?.SpawnUI(UIType.JellyEat);
            if (PlaySFXAudio.Instance != null) PlaySFXAudio.Instance.PlayColorMixSound();
            if (DataManager.Instance.currentScore >= DataManager.Instance.targetScore)
                DataManager.Instance.missions[1].missionCleared = true;
        }

        OnJellyEaten?.Invoke(type);
    }
}