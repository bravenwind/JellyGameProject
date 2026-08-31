using System.Collections;
using UnityEngine;

/// <summary>
/// 씬에 들어올 때 화면을 덮고 있던 검은 이미지를 걷어낸다.
///
/// 화면을 덮는 오브젝트(FadeImage) 자신에게 붙인다 — 그래야 CanvasGroup을
/// GetComponent로 찾을 수 있고, 걷히는 데 걸리는 시간도 그 오브젝트가 갖는다.
/// 인스펙터 배선이 하나도 없다.
///
/// ★ 원래 UIManager 안에 있었다
///   UIManager는 "UIState에 따라 UI 묶음을 켜고 끄는" 클래스였는데,
///   네 개 씬 전부 uiList가 <b>비어 있어서</b> 그 일을 하지 않은 지 오래였다.
///   실제로 살아 있던 건 이 페이드와 "메인으로" 버튼 두 개뿐이었고,
///   둘은 서로 아무 관계가 없다. 관계 없는 둘을 싱글턴 하나가 붙잡고 있을 이유가 없어
///   각자의 자리로 보냈다 (버튼 쪽은 SceneLoader).
///
/// ★ 예전엔 SceneFade(string fadeInOut) 하나로 인/아웃을 갈랐다
///   문자열 비교("FadeIn" / "FadeOut")라 오타가 컴파일에 걸리지 않고 조용히
///   아무 일도 안 하는 구조였다. FadeOut 분기는 부르는 곳이 하나도 없었고,
///   그 안에서 씬 전환까지 호출해 UI 연출과 씬 전환이 엉켜 있었다.
///   실제로 쓰이는 것은 '씬 진입 시 어두운 화면을 걷어내기' 하나뿐이라 그것만 남긴다.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    [Tooltip("화면이 걷히는 데 걸리는 시간 (초)")]
    [SerializeField] private float fadeDuration = 1.0f;

    private void Start()
    {
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        CanvasGroup group = GetComponent<CanvasGroup>();

        group.alpha = 1f;
        group.blocksRaycasts = true;     // 걷히는 동안 입력을 막는다

        float timer = 0f;

        while (timer < fadeDuration)
        {
            //정지 중에도 페이드는 진행돼야 하므로 unscaled를 쓴다
            timer += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(timer / fadeDuration);
            yield return null;
        }

        group.alpha = 0f;
        group.blocksRaycasts = false;    // 걷힌 뒤에는 반드시 입력을 돌려준다
    }
}
