using UnityEngine;

public class JellyColorSource : MonoBehaviour
{
    //파생 클래스(PlayerJellyColorSource)가 직접 쓰므로 protected다.
    //private으로 두면 상속된 쪽에서 보이지 않는다
    [SerializeField] protected Color jellyColor;

    //colorType 을 지웠다. 이 값을 읽던 GetJellyColorType() 이 2a41638 에서 빠졌고,
    //젤리 색 종류는 JellyObject.JellyType 이 정한다. 인스펙터에서 골라도 아무 일도 없던 칸이다
    //(heart bear 프리팹에 남은 colorType 키는 다음 저장 때 유니티가 버린다)

    protected Renderer rend;

    protected virtual void Start()
    {
        rend = GetComponentInChildren<Renderer>();
    }

}
