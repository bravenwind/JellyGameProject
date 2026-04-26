using UnityEngine;

public class JellyObject : MonoBehaviour
{
    [Header("Jelly Info")]
    public string jellyName;
    public JellyColorType jellyType;
    public Vector3Int jellyRGB;

    private void OnEnable() => EntityRegistry.Register(this);
    private void OnDisable() => EntityRegistry.Unregister(this);
}