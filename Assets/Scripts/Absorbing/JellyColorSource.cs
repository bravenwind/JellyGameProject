using UnityEngine;

public class JellyColorSource : MonoBehaviour
{
    public Color jellyColor;

    public JellyColorType colorType;

    protected Renderer rend;

    protected virtual void Start()
    {
        rend = GetComponentInChildren<Renderer>();
    }

}
