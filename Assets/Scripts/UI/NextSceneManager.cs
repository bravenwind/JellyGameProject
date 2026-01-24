using UnityEngine;

public class NextSceneManager : MonoBehaviour
{
    private void Start()
    {
        // 1. 넘어온 로딩 UI를 찾습니다.
        LoadingBGSlideAni loadingAni = FindAnyObjectByType<LoadingBGSlideAni>();

        if (loadingAni != null)
        {
            // 2. 오른쪽으로 스르륵 빠지는 애니메이션 실행 (0.35초)
            loadingAni.SkipHoldAndExit();

            // 3. 빠지는 애니메이션이 끝난 후(약 0.4초 후) 메모리에서 완전 삭제
            Destroy(loadingAni.transform.root.gameObject, 0.4f);
        }
    }
}