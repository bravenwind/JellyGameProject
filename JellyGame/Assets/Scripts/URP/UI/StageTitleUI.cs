using System.Collections;
using UnityEngine;

public class StageTitleUI : MonoBehaviour
{
    public UIManager uiManager;
    public CanvasGroup fadeCanvasGroup;
    public float fadeDuration = 1.0f;
    public float displayDuration = 2.0f; // 글자가 떠 있는 시간

    // void가 아니라 IEnumerator로 변경하면 유니티가 알아서 코루틴으로 실행해줍니다.
    IEnumerator Start()
    {
        // 1. 시작할 때 투명하게 초기화 (알파 0)
        fadeCanvasGroup.alpha = 0f;

        // 2. 나타나기 (Fade In)
        // yield return을 붙이면 이 코루틴이 끝날 때까지 여기서 대기합니다.
        yield return StartCoroutine(uiManager.Fade(fadeCanvasGroup, "FadeOut", fadeDuration));
        // (작성하신 코드 로직상 "FadeOut"이 알파 0->1로 가는 코드라 이걸 씀. *아래 설명 참조)

        // 3. 글자가 떠 있는 시간만큼 대기
        yield return new WaitForSeconds(displayDuration);

        // 4. 사라지기 (Fade Out)
        yield return StartCoroutine(uiManager.Fade(fadeCanvasGroup, "FadeIn", fadeDuration));
        // (작성하신 코드 로직상 "FadeIn"이 알파 1->0으로 가는 코드라 이걸 씀.)
    }
}