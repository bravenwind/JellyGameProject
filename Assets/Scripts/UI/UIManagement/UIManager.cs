using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System;
using JellyNet;

// 1. ���¸� �����ϴ� Enum (�ʿ信 ���� �߰�/�����ϼ���)
public enum UIState
{
    None,       // �ƹ��͵� ���� ����
    Pause,
    Settings,   // ���� â
    InGame,     // ���� �÷��� �� HUD
    GameOver,
    Menu, 
    Help
}

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // 2. �ν����Ϳ��� Enum�� ������Ʈ�� ¦���� �� �ְ� ���� Ŭ����
    [Serializable]
    public class UIStateMapping
    {
        public UIState state;       // � ������ ��?
        public GameObject uiObject; // � UI�� �� ���ΰ�?
    }

    [Header("UI ��� ����")]
    // �� ����Ʈ�� UI ������Ʈ���� ����ϰ� Enum�� �����ϼ���.
    public List<UIStateMapping> uiList = new List<UIStateMapping>();

    [Header("�ʱ� ����")]
    public UIState startState = UIState.InGame;

    [Header("UI ����")]
    [Tooltip("ȭ���� ���� ������ �̹����� CanvasGroup ������Ʈ")]
    public CanvasGroup fadeCanvasGroup;

    [Tooltip("���̵� �� �Ǵ� �ð� (��)")]
    public float fadeDuration = 1.0f;

    // ���� ���¸� �����ϴ� ����
    private UIState currentState;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        StartCoroutine(SceneFade("FadeIn"));
        // ���� ���� �� �ʱ� ���·� ����
        SetState(startState);
    }

    private void Update()
    {
        // �� ������ ���� ���¿� ���� ���� ����
        UpdateState();
    }

    // ==========================================
    // �ٽ� ��� 1: ���� ���� (SetState)
    // ==========================================
    public void SetState(UIState newState)
    {
        currentState = newState;
        Debug.Log($"���� ����: {currentState}");

        // ��ϵ� ��� UI�� ��ȸ�ϸ� ���¿� �´� �͸� �Ѱ�, �������� ���ϴ�.
        foreach (var mapping in uiList)
        {
            if (mapping.uiObject != null)
            {
                // ���� ���¿� ���ε� ���°� ������ true(����), �ƴϸ� false(����)
                bool isActive = (mapping.state == newState);
                mapping.uiObject.SetActive(isActive);
            }
        }

        // ���� ���� �� 1ȸ�� ������ �ʿ��ϴٸ� ���⿡ �ۼ� (��: ���� �ʱ�ȭ ��)
        OnEnterState(newState);
    }

    // ==========================================
    // �ٽ� ��� 2: ���º� ������ ���� (UpdateState)
    // ==========================================
    private void UpdateState()
    {
        switch (currentState)
        {

            case UIState.InGame:
                // ���� �� ����
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    SetState(UIState.Settings);
                }
                break;

            case UIState.Settings:
                // ���� â ����
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    SetState(UIState.InGame);
                }
                break;

            case UIState.GameOver:
                // ���� ���� ����
                //if (Input.GetKeyDown(KeyCode.R))
                //{
                //    SceneManager.LoadScene("LevelDesign");
                //}
                break;

            case UIState.Pause:
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)) 
                {
                    SetState(UIState.InGame);
                }
                break;
        }
    }

    // (���� ����) ���� ���� �� �߰� ó���� ���� �Լ�
    private void OnEnterState(UIState state)
    {
        switch (state)
        {
            case UIState.InGame:
                Time.timeScale = 1f; // ���� �ӵ� ����ȭ
                GUI.enabled = true;
                break;
            case UIState.Settings:
                GUI.enabled = false;
                break;
            case UIState.Pause:
                Time.timeScale = 0f;
                break;
            case UIState.GameOver:
                Time.timeScale = 0f;
                break;
        }
    }

    public IEnumerator SceneFade(string fadeInOut)
    {
        if (fadeCanvasGroup == null) yield break;
        fadeCanvasGroup.blocksRaycasts = true; // �Է� ���� ����

        bool fadeIn = fadeInOut == "FadeIn";
        bool fadeOut = fadeInOut == "FadeOut";

        yield return StartCoroutine(Fade(fadeCanvasGroup, fadeInOut, fadeDuration));

        if (fadeIn)
        {
            fadeCanvasGroup.alpha = 0f; // Ȯ���ϰ� �����ϰ�
            fadeCanvasGroup.blocksRaycasts = false; // [�߿�!] ���̵����� ������ �ݵ�� �Է��� �ٽ� ����ؾ� �մϴ�.
        }
        if (fadeOut)
        {
            Time.timeScale = 1f;
            // 씬만 바꾸면 소켓이 살아남아 다음 판에서 포트가 물리고,
            //   지난 판의 순위·모드가 그대로 남아 새 판에 새어 들어온다.
            //   LanSceneFlow.ToMain이 소켓 종료와 상태 정리를 함께 한다(연결이 없어도 안전).
            LanSceneFlow.ToMain();
        }
    }

    public IEnumerator Fade(CanvasGroup fadeCanvasGroup, string fadeInOut, float fadeDuration)
    {
        float timer = 0f;
        while (timer < fadeDuration)
        {
            // Time.deltaTime ��� unscaledDeltaTime ��� (�Ʒ� 2�� ���� ����)
            timer += Time.unscaledDeltaTime;

            if (fadeInOut == "FadeIn")
            {
                fadeCanvasGroup.alpha = 1 - Mathf.Clamp01(timer / fadeDuration);
            }
            if (fadeInOut == "FadeOut")
            {
                fadeCanvasGroup.alpha = Mathf.Clamp01(timer / fadeDuration);
            }
            yield return null;
        }
    }

    /// <summary>
    /// "메인으로" 버튼. 게임 씬·결과 씬 어디서 눌러도 안전하다.
    ///
    /// [LAN 이식] 소켓 종료와 상태 정리를 LanSceneFlow가 함께 처리한다.
    /// </summary>
    public void OnClick_MainMenuButton()
    {
        LanSceneFlow.ToMain();
    }
}