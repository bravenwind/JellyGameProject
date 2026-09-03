using UnityEngine;

/// <summary>
/// 버튼을 눌러 지정한 오브젝트를 켜거나 끈다. 대상은 <b>인스펙터의 OnClick 인자</b>로 넘긴다.
/// 패널 열기/닫기는 전부 이 하나로 한다.
///
/// ★ 원래는 EnableTargetButton / DisableSelfButton 두 파일이었다
///   ① EnableTargetButton은 켤 대상을 public GameObject target <b>필드</b>로 들고 있었다.
///      그래서 "무엇을 켜는가"가 버튼의 OnClick 목록과 컴포넌트 필드 두 군데로 흩어졌고,
///      컴포넌트 하나가 대상 하나만 가질 수 있었다.
///   ② DisableSelfButton은 자기가 붙어 있는 오브젝트를 껐다. "닫기" 버튼을 만들려면
///      <b>닫히는 패널 쪽</b>에 컴포넌트를 붙여야 해서 켜는 쪽과 방향이 반대였다.
///
///   둘 다 대상을 인자로 받게 고치고 나니 남은 차이가 SetActive의 true/false 하나뿐이라
///   합쳤다. 이제 대상이 인자라 <b>이 컴포넌트는 상태가 없다</b> —
///   씬에 하나만 두고 모든 버튼이 그걸 가리켜도 되고, 지금처럼 흩어져 있어도 된다.
/// </summary>
public class SetActiveButton : MonoBehaviour
{
    /// <summary>대상을 켠다.</summary>
    public void Show(GameObject target)
    {
        Apply(target, true);
    }

    /// <summary>대상을 끈다.</summary>
    public void Hide(GameObject target)
    {
        Apply(target, false);
    }

    private static void Apply(GameObject target, bool active)
    {
        if (target == null)
            return;

        PlaySFXAudio.Instance.PlayButton1Sound();
        target.SetActive(active);
    }
}
