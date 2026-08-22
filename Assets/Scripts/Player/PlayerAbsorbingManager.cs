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

    private void HandleJellyEaten(JellyColorType type)
    {
        colorVisual.HandleJellyAbsorbed(type);
        scaleController.GrowByJelly();
    }
}
