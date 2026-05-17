using UnityEngine;

public class BotBridge : MonoBehaviour
{
    private PlayerScaleController _scaleCtrl;
    private PlayerAbsorber _absorber;

    private void Awake()
    {
        _scaleCtrl = GetComponentInChildren<PlayerScaleController>();
        _absorber = GetComponentInChildren<PlayerAbsorber>();
    }

    private void OnEnable()
    {
        if (_scaleCtrl != null)
            _scaleCtrl.OnPostScalePhysics += HandlePostScalePhysics;
        if (_absorber != null)
            _absorber.OnJellyScored += HandleJellyScored;
    }

    private void OnDisable()
    {
        if (_scaleCtrl != null)
            _scaleCtrl.OnPostScalePhysics -= HandlePostScalePhysics;
        if (_absorber != null)
            _absorber.OnJellyScored -= HandleJellyScored;
    }

    private void HandlePostScalePhysics()
    {
        GetComponent<AIPlayerMovement>()?.RecenterCC();
    }

    private void HandleJellyScored()
    {
        AIPlayerSync aiSync = GetComponentInParent<AIPlayerSync>();
        if (aiSync != null) aiSync.AddScore(DataManager.Instance.scorePerJelly);
    }
}
