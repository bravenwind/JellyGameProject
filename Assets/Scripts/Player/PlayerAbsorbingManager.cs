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

    private void HandleJellyEaten(JellyColorType type)
    {
        // 1. 색 변경
        colorVisual.HandleJellyAbsorbed(type);

        // 2. 크기 변경 체크
        scaleController.CheckScaleUp();
    }

    private void HandleReset()
    {
        colorVisual.ResetColor();
        scaleController.ResetScale();
    }
}
