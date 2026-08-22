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
        AIPlayerMovement bot = GetComponent<AIPlayerMovement>();
        if (bot != null) bot.RecenterCC();
    }

    private void HandleScaleCompleted(float scaleValue)
    {
    }
}
