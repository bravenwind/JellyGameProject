using UnityEngine;
using JellyNet;

public class LocalOwnerFeedback : MonoBehaviour
{
    private PlayerScaleController scaleController;
    private PlayerAbsorber absorber;
    private PlayerMovement movement;

    private NetIdentity netId;

    private bool IsLocalOwner
    {
        get
        {
            return netId != null && netId.IsMine;
        }
    }

    private void Awake()
    {
        netId = GetComponentInParent<NetIdentity>();
        scaleController = GetComponentInChildren<PlayerScaleController>();
        absorber = GetComponentInChildren<PlayerAbsorber>();
        movement = GetComponentInChildren<PlayerMovement>();
    }

    private void OnEnable()
    {
        if (scaleController != null)
        {
            scaleController.OnScaleGrowStarted += HandleScaleGrowStarted;
            scaleController.OnScaleCrossedThreshold += HandleScaleCrossedThreshold;
            scaleController.OnScaleSettled += HandleScaleSettled;
        }

        if (absorber != null)
            absorber.OnJellyScored += HandleJellyScored;
    }

    private void OnDisable()
    {
        if (scaleController != null)
        {
            scaleController.OnScaleGrowStarted -= HandleScaleGrowStarted;
            scaleController.OnScaleCrossedThreshold -= HandleScaleCrossedThreshold;
            scaleController.OnScaleSettled -= HandleScaleSettled;
        }

        if (absorber != null)
            absorber.OnJellyScored -= HandleJellyScored;
    }

    private void HandleScaleGrowStarted(bool playEffect)
    {
        if (!playEffect)
            return;

        if (!IsLocalOwner)
            return;

        if (PlaySFXAudio.Instance == null)
            return;

        PlaySFXAudio.Instance.PlayScaleUpSound();

        if (GameState.CurrentGameMode == GameModeType.Absorb)
            PlaySFXAudio.Instance.PlayColorMixSound();
    }

    private void HandleScaleCrossedThreshold()
    {
        if (!IsLocalOwner)
            return;
        GameState.OnCameraScaleIncreased?.Invoke();
    }

    private void HandleScaleSettled(float scaleValue)
    {
        if (!IsLocalOwner)
            return;
        if (movement != null)
        {
            movement.JumpForce = scaleValue >= DataManager.Instance.JumpScaleThreshold
                ? movement.OriginalJumpForce + DataManager.Instance.IncreaseJumpForceValue
                : movement.OriginalJumpForce;
        }
        GameState.PlayerCurrentScale = scaleValue;

        GameState.CurrentScore = NetEntity.ScoreFromScale(scaleValue);
    }

    private void HandleJellyScored()
    {
        if (!IsLocalOwner)
            return;

        var dm = DataManager.Instance;
        float predictedScale = scaleController != null
            ? scaleController.PendingScale
            : GameState.PlayerCurrentScale + dm.JellyScaleIncrease;

        GameState.CurrentScore = NetEntity.ScoreFromScale(predictedScale);

        if (PlaySFXAudio.Instance != null)
            PlaySFXAudio.Instance.PlayColorMixSound();
    }
}
