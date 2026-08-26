using UnityEngine;

public class PlayerAbsorbingManager : MonoBehaviour
{
    [Header("Player Parts")]
    [SerializeField] private PlayerAbsorber absorber;
    [SerializeField] private PlayerColorVisual colorVisual;
    [SerializeField] private PlayerScaleController scaleController;

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
