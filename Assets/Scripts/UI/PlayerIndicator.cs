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
[RequireComponent(typeof(RectTransform))]
public class PlayerIndicator : MonoBehaviour
{
    [Tooltip("삼각형 이미지. 색은 대상 플레이어의 색으로 매 프레임 덮어쓴다.")]
    [SerializeField] private Image image;

    private RectTransform rect;

    /// <summary>화면 좌표를 직접 쓰기 위해 RectTransform을 그대로 내준다.</summary>
    public RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    /// <summary>이 삼각형이 가리키는 개체. 사람이든 봇이든 INetEntity 하나로 들어온다.</summary>
    public INetEntity Entity { get; set; }

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
