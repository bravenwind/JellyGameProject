using UnityEngine;

/// <summary>
/// 버튼을 눌러 지정한 오브젝트를 끈다. 끌 대상은 <b>인스펙터의 OnClick 인자</b>로 넘긴다.
/// EnableTargetButton과 짝이다.
///
/// ★ 예전 이름은 DisableSelfButton이었고, 자기가 붙어 있는 오브젝트를 껐다
///   그래서 "닫기" 버튼을 만들려면 <b>닫히는 패널 쪽</b>에 컴포넌트를 붙이고
///   버튼의 OnClick이 그 패널을 가리키게 해야 했다 — 켜는 쪽(EnableTargetButton)과
///   방향이 반대라 매번 헷갈렸고, 한 패널이 두 곳에서 닫히면 대상이 자기 자신으로
///   고정돼 있어 응용이 안 됐다.
///   인자로 받으면 켜는 쪽과 모양이 같아지고, OnClick 줄만 보면 무엇이 닫히는지 보인다.
///   'Self'가 더는 사실이 아니라 이름도 바꿨다 (guid는 유지 — 기존 배선이 그대로 산다).
/// </summary>
public class DisableTargetButton : MonoBehaviour
{
    public void DisableTarget(GameObject target)
    {
        if (target == null)
            return;

        PlaySFXAudio.Instance.PlayButton1Sound();
        target.SetActive(false);
    }
}
