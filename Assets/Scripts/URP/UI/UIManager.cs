using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

// 1. 상태를 정의하는 Enum (필요에 따라 추가/수정하세요)
public enum UIState
{
    None,       // 아무것도 없는 상태
    Pause,
    //MainMenu,   // 메인 메뉴
    Settings,   // 설정 창
    InGame,     // 게임 플레이 중 HUD
    GameOver,    // 게임 오버 창
    Menu
}

public class UIManager : MonoBehaviour
{
    // 2. 인스펙터에서 Enum과 오브젝트를 짝지을 수 있게 만든 클래스
    [System.Serializable]
    public class UIStateMapping
    {
        public UIState state;       // 어떤 상태일 때?
        public GameObject uiObject; // 어떤 UI를 켤 것인가?
    }

    [Header("UI 등록 설정")]
    // 이 리스트에 UI 오브젝트들을 등록하고 Enum을 지정하세요.
    public List<UIStateMapping> uiList = new List<UIStateMapping>();

    [Header("초기 상태")]
    public UIState startState = UIState.InGame;

    [Header("UI 설정")]
    [Tooltip("화면을 가릴 검은색 이미지의 CanvasGroup 컴포넌트")]
    public CanvasGroup fadeCanvasGroup;

    [Tooltip("페이드 인 되는 시간 (초)")]
    public float fadeDuration = 1.0f;

    // 현재 상태를 저장하는 변수
    private UIState currentState;

    private void Start()
    {
        StartCoroutine(SceneFade("FadeIn"));
        // 게임 시작 시 초기 상태로 진입
        SetState(startState);
    }

    private void Update()
    {
        // 매 프레임 현재 상태에 따른 로직 실행
        UpdateState();
    }

    // ==========================================
    // 핵심 기능 1: 상태 변경 (SetState)
    // ==========================================
    public void SetState(UIState newState)
    {
        currentState = newState;
        Debug.Log($"상태 변경: {currentState}");

        // 등록된 모든 UI를 순회하며 상태에 맞는 것만 켜고, 나머지는 끕니다.
        foreach (var mapping in uiList)
        {
            if (mapping.uiObject != null)
            {
                // 현재 상태와 매핑된 상태가 같으면 true(켜짐), 아니면 false(꺼짐)
                bool isActive = (mapping.state == newState);
                mapping.uiObject.SetActive(isActive);
            }
        }

        // 상태 진입 시 1회성 로직이 필요하다면 여기에 작성 (예: 점수 초기화 등)
        OnEnterState(newState);
    }

    // ==========================================
    // 핵심 기능 2: 상태별 프레임 로직 (UpdateState)
    // ==========================================
    private void UpdateState()
    {
        switch (currentState)
        {
            //case UIState.MainMenu:
            //    // 메인 메뉴에서의 로직 (예: 아무 키나 누르면 게임 시작)
            //    if (Input.GetKeyDown(KeyCode.Space))
            //    {
            //        Debug.Log("게임 시작!");
            //        SetState(UIState.InGame);
            //    }
            //    break;

            case UIState.InGame:
                // 게임 중 로직 (예: ESC 누르면 일시정지/설정)
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    SetState(UIState.Settings);
                }
                break;

            case UIState.Settings:
                // 설정 창 로직 (예: ESC 누르면 다시 게임으로)
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    SetState(UIState.InGame);
                }
                break;

            case UIState.GameOver:
                // 게임 오버 로직 (예: R키로 재시작)
                if (Input.GetKeyDown(KeyCode.R))
                {
                    SceneManager.LoadScene("TitleScene");
                }
                break;

            case UIState.Pause:
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)) 
                {
                    SetState(UIState.InGame);
                }
                break;
        }
    }

    // (선택 사항) 상태 진입 시 추가 처리를 위한 함수
    private void OnEnterState(UIState state)
    {
        switch (state)
        {
            case UIState.InGame:
                Time.timeScale = 1f; // 게임 속도 정상화
                GUI.enabled = true;
                break;
            case UIState.Settings:
                Time.timeScale = 0f; // 게임 일시 정지
                GUI.enabled = false;
                break;
            case UIState.Pause:
                Time.timeScale = 0f;
                break;
        }
    }

    public IEnumerator SceneFade(string fadeInOut)
    {
        if (fadeCanvasGroup == null) yield break;
        fadeCanvasGroup.blocksRaycasts = true; // 입력 차단 시작

        bool fadeIn = fadeInOut == "FadeIn";
        bool fadeOut = fadeInOut == "FadeOut";

        yield return StartCoroutine(Fade(fadeCanvasGroup, fadeInOut, fadeDuration));

        if (fadeIn)
        {
            fadeCanvasGroup.alpha = 0f; // 확실하게 투명하게
            fadeCanvasGroup.blocksRaycasts = false; // [중요!] 페이드인이 끝나면 반드시 입력을 다시 허용해야 합니다.
        }
        if (fadeOut)
        {
            SceneManager.LoadScene("TitleScene");
        }
    }

    public IEnumerator Fade(CanvasGroup fadeCanvasGroup, string fadeInOut, float fadeDuration)
    {
        float timer = 0f;
        while (timer < fadeDuration)
        {
            // Time.deltaTime 대신 unscaledDeltaTime 사용 (아래 2번 이유 참조)
            timer += Time.unscaledDeltaTime;

            if (fadeInOut == "FadeIn")
            {
                fadeCanvasGroup.alpha = 1 - Mathf.Clamp01(timer / fadeDuration);
                Debug.Log(fadeCanvasGroup.alpha);
            }
            if (fadeInOut == "FadeOut")
            {
                fadeCanvasGroup.alpha = Mathf.Clamp01(timer / fadeDuration);
            }
            yield return null;
        }
    }
}