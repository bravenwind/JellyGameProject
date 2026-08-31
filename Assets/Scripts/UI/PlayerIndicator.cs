using UnityEngine;
using UnityEngine.UI;
using JellyNet;

/// <summary>
/// 화면 밖 플레이어를 가리키는 삼각형 하나. 프리팹 한 벌의 얼굴이다.
///
/// ★ 예전엔 이게 클래스도 프리팹도 아니었다
///   OffScreenPlayerIndicator 안의 private class와, 거기서 GameObject를 손으로 조립하는
///   CreateIndicator() 20줄로 나뉘어 있었다. 그래서 삼각형 하나의 생김새를 바꾸려면
///   <b>코드를 고쳐야</b> 했고, 크기·외곽선·색 같은 값이 관리자 스크립트에 섞여 있었다.
///
///   프리팹으로 빼면 생김새는 에디터에서, 배치 규칙은 코드에서 정해진다.
///   같은 일을 하는 MinimapArrowManager가 이미 arrowPrefab + MinimapArrow 구조다.
///
/// 이 컴포넌트가 직접 하는 일은 없다 — 위치·회전·색은 OffScreenPlayerIndicator가 정한다.
/// 여기 있는 건 <b>그 관리자가 매 프레임 GetComponent를 하지 않게 하는 손잡이</b>다.
/// </summary>
// ★ [RequireComponent(typeof(RectTransform))]를 붙이지 않는다
//   유니티는 일반 Transform을 가진 오브젝트에 RectTransform을 <b>추가할 수 없다.</b>
//   그래서 이 어트리뷰트가 붙어 있으면 UI가 아닌 오브젝트에 이 스크립트를 드래그할 때
//   유니티가 조용히 거부한다 — "컴포넌트가 안 붙는" 증상의 정체다.
//   방어가 목적이었는데 오히려 손을 묶었다. 아래 Rect가 안전하게 처리한다.
public class PlayerIndicator : MonoBehaviour
{
    [Tooltip("삼각형 이미지. 비워두면 자기 자신과 자식에서 찾는다.")]
    [SerializeField] private Image image;

    private RectTransform rect;

    /// <summary>
    /// 화면 좌표를 직접 쓰기 위해 RectTransform을 그대로 내준다.
    /// UI가 아닌 곳에 붙었으면 null — 그래도 예외로 죽지는 않는다.
    /// </summary>
    public RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = transform as RectTransform;
            return rect;
        }
    }

    /// <summary>이 삼각형이 가리키는 개체. 사람이든 봇이든 INetEntity 하나로 들어온다.</summary>
    public INetEntity Entity { get; set; }

    //인스펙터 연결을 잊어도 동작하게 한다. 프리팹에는 이미 연결돼 있으므로 보통 그냥 지나간다.
    private void Awake()
    {
        if (image == null)
            image = GetComponentInChildren<Image>(true);
    }

    /// <summary>대상의 현재 색. 개체가 없으면 흰색.</summary>
    public void ApplyColor()
    {
        if (image == null)
            return;

        Color c = Entity != null ? Entity.VisualColor : Color.white;
        c.a = 1f;
        image.color = c;
    }
}
