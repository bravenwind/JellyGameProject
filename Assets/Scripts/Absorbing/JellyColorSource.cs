using UnityEngine;

public class JellyColorSource : MonoBehaviour
{
    //파생 클래스(PlayerJellyColorSource)가 직접 쓰므로 protected다.
    //private으로 두면 상속된 쪽에서 보이지 않는다
    [SerializeField] protected Color jellyColor;

    [SerializeField] private JellyColorType colorType;

    protected Renderer rend;

    protected virtual void Start()
    {
        rend = GetComponentInChildren<Renderer>();
    }

}
