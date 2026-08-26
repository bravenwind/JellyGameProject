using UnityEngine;

public class JellyObject : MonoBehaviour
{
    [Header("Jelly Info")]
    [SerializeField] private JellyColorType jellyType;
    public JellyColorType JellyType { get { return jellyType; } }

    private void OnEnable() => EntityRegistry.Register(this);
    private void OnDisable() => EntityRegistry.Unregister(this);
}