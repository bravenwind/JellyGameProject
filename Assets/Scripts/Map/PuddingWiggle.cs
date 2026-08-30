using UnityEngine;

//푸딩이 흔들리는 건 연출뿐이라 각자 로컬에서 재생한다.
//예전엔 RPC로 전파했는데, 그러면 소유자만 트리거를 쏠 수 있어
//원격 플레이어가 밟았을 때는 아무 화면에서도 안 흔들렸다
public class PuddingWiggle : MonoBehaviour
{
    [SerializeField] private Animator puddingAnimator;

    private void OnTriggerEnter(Collider other)
    {
        //캐릭터는 트리거 콜라이더가 둘이라 대표 하나만 받는다.
        //안 걸면 한 번 밟을 때 SetTrigger가 두 번 들어간다.
        //(GameTags.IsCharacterMainCollider 주석 참고)
        if (!GameTags.IsCharacterMainCollider(other))
            return;

        if (puddingAnimator != null)
            puddingAnimator.SetTrigger(AnimParams.Wiggle);
    }
}
