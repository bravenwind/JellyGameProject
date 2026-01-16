using UnityEngine;

public enum JellyColorType
{
    Red, Green, Blue,
    Yellow, Cyan, Magenta, White, Black
}

public class JellyColorSource : MonoBehaviour
{
    public Color jellyColor;

    public JellyColorType colorType;

    protected Renderer rend;

    protected virtual void Start()
    {
        rend = GetComponentInChildren<Renderer>();
    }

    public JellyColorType GetJellyColorType()
    {
        return colorType;
    }

    public Color GetColor()
    {
        return jellyColor;
    }
}
