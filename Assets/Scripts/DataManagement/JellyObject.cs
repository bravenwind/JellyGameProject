using UnityEngine;

public class JellyObject : MonoBehaviour
{
    [Header("Jelly Info")]
    public string jellyName;
    public JellyColorType jellyType;
    public Vector3Int jellyRGB;

    // 젤리가 생성되거나 활성화될 때 머티리얼 세팅을 확실하게 하기 위함
    public void Initialize(string name, JellyColorType type)
    {
        this.jellyName = name;
        this.jellyType = type;
    }
}