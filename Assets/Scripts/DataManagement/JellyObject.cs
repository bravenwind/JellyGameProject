using UnityEngine;

public class JellyObject : MonoBehaviour
{
    [Header("Jelly Info")]
    public JellyColorType jellyType;

    private void OnEnable() => EntityRegistry.Register(this);
    private void OnDisable() => EntityRegistry.Unregister(this);
}