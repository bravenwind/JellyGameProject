using UnityEngine;

public class PlayerAbsorbingManager : MonoBehaviour
{
    [Header("Player Parts")]
    public PlayerAbsorber absorber;
    public PlayerColorVisual colorVisual;
    public PlayerScaleController scaleController;

    private void OnEnable()
    {
        absorber.OnJellyEaten += HandleJellyEaten;
    }

    private void OnDisable()
    {
        absorber.OnJellyEaten -= HandleJellyEaten;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            HandleReset();
        }
    }

    public bool isBot = false;
    private int _botAbsorbCount = 0;

    private void HandleJellyEaten(JellyColorType type)
    {
        colorVisual.HandleJellyAbsorbed(type);

        if (isBot)
        {
            _botAbsorbCount++;
            if (_botAbsorbCount >= DataManager.Instance.scaleLevelUpExp)
            {
                _botAbsorbCount = 0;
                scaleController.BotLevelUp();
            }
        }
        else
        {
            scaleController.CheckScaleUp();
        }
    }

    private void HandleReset()
    {
        colorVisual.ResetColor();
        scaleController.ResetScale();
    }
}
