using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    [Header("UI 설정")]
    [Tooltip("화면을 가릴 검은색 이미지의 CanvasGroup 컴포넌트")]
    public CanvasGroup fadeCanvasGroup;

    [Tooltip("페이드 아웃되는 시간 (초)")]
    public float fadeDuration = 1.0f;

    public Memory memory;

    private bool isFading = false; // 중복 실행 방지용 플래그

    private void Start()
    {
        // 시작할 때는 화면이 보여야 하므로 Alpha를 0으로 초기화
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false; // 평소에는 클릭 방해 안 하게
            if (memory.enableFadeIn) 
            {
                StartCoroutine(FadeIn());
            }
        }
    }

    private void Update()
    {
        // 페이드 중이 아닐 때만 입력 받음
        if (!isFading && Input.anyKeyDown)
        {
            StartCoroutine(FadeOutAndLoadScene("TileScene"));
        }
    }

    // 페이드 아웃 코루틴
    private IEnumerator FadeOutAndLoadScene(string sceneName)
    {
        isFading = true; // 중복 입력 방지

        if (fadeCanvasGroup == null)
        {
            Debug.LogWarning("CanvasGroup이 연결되지 않아 즉시 이동합니다.");
            SceneManager.LoadScene(sceneName);
            yield break;
        }

        // 레이캐스트를 켜서 페이드 중에는 버튼 클릭 등 다른 조작 막기
        fadeCanvasGroup.blocksRaycasts = true;

        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            // 0(투명) -> 1(검정)로 점차 변경
            fadeCanvasGroup.alpha = Mathf.Clamp01(timer / fadeDuration);
            yield return null; // 한 프레임 대기
        }

        memory.enableFadeIn = true;

        // 완전히 검어지면 씬 로드
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator FadeIn()
    {
        isFading = true; // 중복 입력 방지

        // 레이캐스트를 켜서 페이드 중에는 버튼 클릭 등 다른 조작 막기
        fadeCanvasGroup.blocksRaycasts = true;

        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            // 0(투명) -> 1(검정)로 점차 변경
            fadeCanvasGroup.alpha = 1 -  Mathf.Clamp01(timer / fadeDuration);
            yield return null; // 한 프레임 대기
        }

        fadeCanvasGroup.alpha = 0;
    }
}