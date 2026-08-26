using System.Collections;
using UnityEngine;

public class LoadingPanelAni : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject anyKeyPanel;     // AnyKeyPanel
    [SerializeField] private GameObject loadingPanel;    // LoadingPanel
    [SerializeField] private GameObject mainMenuPanel;   // MainMenuPanel

    [Header("Timing")]
    [SerializeField] private float loadingSeconds = 2.5f; // 2~3초

    [Header("Options")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool allowMouseClick = true; // false면 키보드만

    private bool fired;
    private Coroutine routine;

    private void Start()
    {
        if (!playOnStart)
            return;

        if (anyKeyPanel != null)
            anyKeyPanel.SetActive(true);
        if (loadingPanel != null)
            loadingPanel.SetActive(false);
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
    }

    private void Update()
    {
        if (fired)
            return;

        if (IsAnyKeyPressed())
        {
            fired = true;
            routine = StartCoroutine(CoSwitchFlow());
        }
    }

    private bool IsAnyKeyPressed()
    {
        if (allowMouseClick)
            return Input.anyKeyDown;

        return !string.IsNullOrEmpty(Input.inputString);
    }

    private IEnumerator CoSwitchFlow()
    {
        // 1) 로딩 패널 켜기
        if (loadingPanel != null)
            loadingPanel.SetActive(true);

        // 2) 홀드 구간 동안(=로딩 중) AnyKey는 끄고 MainMenu는 켜기
        if (anyKeyPanel != null)
            anyKeyPanel.SetActive(false);
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);

        // 3) 지정 시간 대기 (타임스케일 무시)
        float t = 0f;
        while (t < loadingSeconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // 4) 로딩 패널 끄기 (메인메뉴는 그대로)
        if (loadingPanel != null)
            loadingPanel.SetActive(false);

        routine = null;
    }

    private void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        fired = false;
    }
}
