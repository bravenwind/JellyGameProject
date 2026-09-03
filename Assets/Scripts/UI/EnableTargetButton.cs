using UnityEngine;

/// <summary>
/// 버튼을 눌러 지정한 오브젝트를 켠다. 켤 대상은 <b>인스펙터의 OnClick 인자</b>로 넘긴다.
///
/// ★ 예전엔 public GameObject target 필드를 컴포넌트가 들고 있었다
///   그래서 "무엇을 켜는가"가 두 군데에 흩어졌다 —
///   버튼의 OnClick 목록에는 EnableTarget()만 적혀 있고, 정작 대상은
///   그 버튼에 붙은 <b>다른 컴포넌트의 필드</b>를 열어봐야 알 수 있었다.
///   게다가 컴포넌트 하나가 대상 하나만 가질 수 있어, 한 버튼이 둘을 켜려면
///   컴포넌트를 두 개 붙여야 했다.
///   인자로 받으면 OnClick 줄 하나만 보면 "이 버튼이 무엇을 켜는지"가 다 보인다.
/// </summary>
public class EnableTargetButton : MonoBehaviour
{
    public void EnableTarget(GameObject target)
    {
        if (target == null)
            return;

        PlaySFXAudio.Instance.PlayButton1Sound();
        target.SetActive(true);
    }
}
