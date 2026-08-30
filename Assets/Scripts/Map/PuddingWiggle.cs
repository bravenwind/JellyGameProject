using UnityEngine;

//푸딩이 흔들리는 건 연출뿐이라 각자 로컬에서 재생한다.
//예전엔 RPC로 전파했는데, 그러면 소유자만 트리거를 쏠 수 있어
//원격 플레이어가 밟았을 때는 아무 화면에서도 안 흔들렸다
public class PuddingWiggle : MonoBehaviour
{
    [SerializeField] private Animator puddingAnimator;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(GameTags.PlayerMesh))
            return;

        if (puddingAnimator != null)
            puddingAnimator.SetTrigger(AnimParams.Wiggle);
    }
}
