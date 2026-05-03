using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

public class LobbyController : MonoBehaviourPunCallbacks, IOnEventCallback
{
    [Header("입력 패널")]
    public RectTransform inputPanel;
    public TMP_InputField nameInputField;
    public Button startButton;

    [Header("매칭 UI")]
    public RectTransform matchingPanel;
    public TMP_Text matchingStatusText;     // "매칭 중..." → "매칭 완료!"
    public TMP_Text currentPlayerCountText; // "2 / 4명"

    [Header("카운트다운 UI")]
    public TMP_Text countdownText;  // matchingStatusText 자리에 나타날 숫자
    public TMP_Text gameStartText;  // "게임 시작!"

    [Header("애니메이션 설정")]
    [SerializeField] private Vector2 inputPanelLeftPos = new Vector2(-400f, 0f);
    [SerializeField] private float slideDuration = 0.45f;
    [SerializeField] private Ease slideEase = Ease.OutCubic;
    [SerializeField] private float matchingPopDelay = 0.2f;
    [SerializeField] private float matchingPopDuration = 0.35f;
    [SerializeField] private Ease matchingPopEase = Ease.OutBack;
    [SerializeField] private float matchingCompleteSlideY = 60f; // 매칭 완료 후 올라가는 거리

    private Vector2 _inputPanelOriginPos;
    private Vector2 _matchingStatusOriginPos; // matchingStatusText 원래 위치
    private Coroutine _matchingTextCoroutine;
    private bool _countdownStarted = false;

    private void Start()
    {
        if (inputPanel != null)
            _inputPanelOriginPos = inputPanel.anchoredPosition;

        if (matchingStatusText != null)
            _matchingStatusOriginPos = matchingStatusText.rectTransform.anchoredPosition;

        if (currentPlayerCountText != null)
        {
            currentPlayerCountText.text = $"(? / {NetworkManager.Instance.maxPlayersPerRoom})";
        }

        if (matchingPanel != null)
        {
            matchingPanel.localScale = Vector3.zero;
            matchingPanel.gameObject.SetActive(false);
        }

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);

        if (gameStartText != null)
            gameStartText.gameObject.SetActive(false);

        startButton.onClick.AddListener(OnStartButtonClicked);
    }

    // ── IOnEventCallback ──────────────────────────────────────

    public override void OnEnable()
    {
        base.OnEnable();
        PhotonNetwork.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        base.OnDisable();
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public void OnEvent(EventData photonEvent)
    {
        switch (photonEvent.Code)
        {
            case NetworkManager.EVENT_PLAYER_COUNT:
                UpdatePlayerCount((int)photonEvent.CustomData);
                break;
            case NetworkManager.EVENT_COUNTDOWN:
                ShowCountdown((int)photonEvent.CustomData);
                break;
            case NetworkManager.EVENT_GAME_START:
                ShowGameStart();
                break;
        }
    }

    // ── UI 업데이트 ───────────────────────────────────────────

    public void UpdatePlayerCount(int count)
    {
        if (currentPlayerCountText != null)
            currentPlayerCountText.text = $"({count} / {NetworkManager.Instance.maxPlayersPerRoom})";
    }

    private void ShowCountdown(int number)
    {
        // 첫 카운트다운 수신 시에만 매칭 완료 연출 실행
        if (!_countdownStarted)
        {
            _countdownStarted = true;
            PlayMatchingCompleteAnimation();
        }

        if (countdownText == null) return;

        countdownText.text = number.ToString();

        // 숫자 교체마다 스케일 팡
        countdownText.transform.localScale = Vector3.zero;
        countdownText.transform
            .DOScale(Vector3.one, 0.3f)
            .SetEase(Ease.OutBack)
            .SetUpdate(true);
    }

    private void PlayMatchingCompleteAnimation()
    {
        // "매칭 중..." 코루틴 정지
        if (_matchingTextCoroutine != null)
        {
            StopCoroutine(_matchingTextCoroutine);
            _matchingTextCoroutine = null;
        }

        Sequence seq = DOTween.Sequence().SetUpdate(true);

        // 1. "매칭 완료!" 텍스트로 교체하면서 위로 이동
        if (matchingStatusText != null)
        {
            seq.AppendCallback(() => matchingStatusText.text = "매칭 완료!");
            seq.Append(
                matchingStatusText.rectTransform
                    .DOAnchorPos(_matchingStatusOriginPos + new Vector2(0f, matchingCompleteSlideY), 0.4f)
                    .SetEase(Ease.OutCubic)
            );
        }

        // 2. matchingStatusText가 올라가는 동안 카운트다운 텍스트가 원래 자리에서 팝
        if (countdownText != null)
        {
            // 원래 matchingStatusText 위치에 배치
            countdownText.rectTransform.anchoredPosition = _matchingStatusOriginPos;
            countdownText.transform.localScale = Vector3.zero;
            seq.AppendCallback(() => countdownText.gameObject.SetActive(true));
            seq.Join(
                countdownText.transform
                    .DOScale(Vector3.one, 0.35f)
                    .SetDelay(0.15f) // 살짝 텀을 두고 등장
                    .SetEase(Ease.OutBack)
            );
        }
    }

    private void ShowGameStart()
    {
        // 카운트다운 숨기기
        if (countdownText != null)
        {
            countdownText.transform
                .DOScale(Vector3.zero, 0.2f)
                .SetUpdate(true)
                .OnComplete(() => countdownText.gameObject.SetActive(false));
        }

        if (gameStartText == null) return;

        gameStartText.gameObject.SetActive(true);
        gameStartText.transform.localScale = Vector3.zero;
        gameStartText.transform
            .DOScale(Vector3.one, 0.4f)
            .SetEase(Ease.OutBack)
            .SetUpdate(true);
    }

    // 연결 실패 시 버튼 복원
    public override void OnDisconnected(DisconnectCause cause)
    {
        _countdownStarted = false;
        startButton.interactable = true;

        if (matchingPanel != null)
        {
            matchingPanel.transform
                .DOScale(Vector3.zero, 0.2f)
                .SetUpdate(true)
                .OnComplete(() => matchingPanel.gameObject.SetActive(false));
        }
        if (inputPanel != null)
            inputPanel.DOAnchorPos(_inputPanelOriginPos, 0.3f).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    // ── 버튼 / 매칭 애니메이션 ────────────────────────────────

    private void OnStartButtonClicked()
    {
        startButton.interactable = false;

        string playerName = nameInputField != null && !string.IsNullOrEmpty(nameInputField.text)
            ? nameInputField.text
            : "Jelly";

        PlayAnimation(playerName);
    }

    private void PlayAnimation(string playerName)
    {
        Sequence seq = DOTween.Sequence().SetUpdate(true);

        if (inputPanel != null)
            seq.Append(inputPanel.DOAnchorPos(inputPanelLeftPos, slideDuration).SetEase(slideEase));

        if (matchingPanel != null)
        {
            seq.AppendInterval(matchingPopDelay);
            seq.AppendCallback(() =>
            {
                matchingPanel.gameObject.SetActive(true);

                if (_matchingTextCoroutine != null) StopCoroutine(_matchingTextCoroutine);
                _matchingTextCoroutine = StartCoroutine(AnimateMatchingText());
            });
            seq.Append(matchingPanel.DOScale(Vector3.one, matchingPopDuration).SetEase(matchingPopEase));
        }

        seq.OnComplete(() => NetworkManager.Instance?.StartConnect(playerName));
    }

    private IEnumerator AnimateMatchingText()
    {
        if (matchingStatusText == null) yield break;

        int dotCount = 1;
        while (true)
        {
            matchingStatusText.text = "매칭 중" + new string('.', dotCount);
            dotCount = dotCount % 3 + 1;
            yield return new WaitForSecondsRealtime(0.4f);
        }
    }
}