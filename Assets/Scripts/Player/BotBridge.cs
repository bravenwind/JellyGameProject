using UnityEngine;

public class BotBridge : MonoBehaviour
{
    private PlayerScaleController _scaleCtrl;

    private void Awake()
    {
        _scaleCtrl = GetComponentInChildren<PlayerScaleController>();
    }

    private void OnEnable()
    {
        if (_scaleCtrl != null)
        {
            _scaleCtrl.OnPostScalePhysics += HandlePostScalePhysics;
            _scaleCtrl.OnScaleCompleted += HandleScaleCompleted;
        }
    }

    private void OnDisable()
    {
        if (_scaleCtrl != null)
        {
            _scaleCtrl.OnPostScalePhysics -= HandlePostScalePhysics;
            _scaleCtrl.OnScaleCompleted -= HandleScaleCompleted;
        }
    }

    private void HandlePostScalePhysics()
    {
        GetComponent<AIPlayerMovement>()?.RecenterCC();
    }

    private void HandleScaleCompleted(float scaleValue)
    {
        AIPlayerSync aiSync = GetComponentInParent<AIPlayerSync>();
        if (aiSync != null) aiSync.SetScoreFromScale(scaleValue);
    }
}
