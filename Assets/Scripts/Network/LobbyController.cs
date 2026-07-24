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
    private string playerNickname;
    public int nicknameMaxLength = 10;
    public Button startButton;
    public GameObject warningText;

    [Tooltip("매칭 취소 버튼 — 매칭 완료(카운트다운 시작) 시 비활성화, 메인 씬 활성화/매칭 시작 시 활성화")]
    public Button cancelMatchingButton;

    [Header("모드 선택 패널")]
    public GameObject buttonSelectionPanel;

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

    [Header("가짜 매칭 연출")]
    [SerializeField] private float fakeJoinIntervalMin = 0.8f; // 가짜 플레이어 합류 최소 간격
    [SerializeField] private float fakeJoinIntervalMax = 2.5f; // 가짜 플레이어 합류 최대 간격
    private int _displayedCount = 0;          // 화면에 표시되는 인원 (실제와 무관)
    private Coroutine _fakeCounterCoroutine;

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

        warningText?.SetActive(false);

        // 메인 씬 활성화 시 매칭 취소 버튼을 활성화(다시 매칭할 수 있는 상태).
        if (cancelMatchingButton != null) cancelMatchingButton.gameObject.SetActive(true);

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
            case NetworkManager.EVENT_BEGIN_CURTAIN:
                // [완전판] 비마스터도 Main(출발 씬)에서 로딩 커튼 슬라이드인을 시작한다(로드는 마스터가 주도).
                // 프리팹 없으면 no-op → 마스터의 LoadLevel(Loading)에 끌려가 기존 방식(Loading 커튼)으로 폴백.
                LoadingSceneController.TryBeginDepartureIntro();
                break;
        }
    }

    // ── UI 업데이트 ───────────────────────────────────────────

    private void ShowCountdown(int number)
    {
        // 첫 카운트다운 수신 시에만 매칭 완료 연출 실행
        if (!_countdownStarted)
        {
            _countdownStarted = true;

            // 매칭 완료 → 이제 취소 불가. 매칭 취소 버튼 비활성화.
            if (cancelMatchingButton != null) cancelMatchingButton.gameObject.SetActive(false);

            // 가짜 카운터 중지
            if (_fakeCounterCoroutine != null)
            {
                StopCoroutine(_fakeCounterCoroutine);
                _fakeCounterCoroutine = null;
            }

            // 인원을 max로 채워서 "꽉 찬 것처럼" 보임
            _displayedCount = NetworkManager.Instance.maxPlayersPerRoom;
            UpdatePlayerCountUI();

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
        playerNickname = nameInputField != null && !string.IsNullOrEmpty(nameInputField.text)
            ? nameInputField.text
            : "Jelly";

        if (playerNickname.Length > nicknameMaxLength)
        {
            warningText?.SetActive(true);
            return;
        }
        else
        {
            warningText?.SetActive(false);
            buttonSelectionPanel?.SetActive(true);
        }
    }

    public void OnClickPushMode()
    {
        startButton.interactable = false;
        NetworkManager.SelectedGameMode = GameModeType.Push;
        PlayAnimation(playerNickname);
    }
    public void OnClickAbsorbMode()
    {
        startButton.interactable = false;
        NetworkManager.SelectedGameMode = GameModeType.Absorb;
        PlayAnimation(playerNickname);
    }

    private void PlayAnimation(string playerName)
    {
        Sequence seq = DOTween.Sequence().SetUpdate(true);

        if (matchingPanel != null)
        {
            seq.AppendInterval(matchingPopDelay);
            seq.AppendCallback(() =>
            {
                matchingPanel.gameObject.SetActive(true);

                // 매칭 시작 → 취소 가능 상태로(이전 매칭에서 비활성화됐을 수 있으므로 복원).
                if (cancelMatchingButton != null) cancelMatchingButton.gameObject.SetActive(true);

                if (_matchingTextCoroutine != null) StopCoroutine(_matchingTextCoroutine);
                _matchingTextCoroutine = StartCoroutine(AnimateMatchingText());
            });
            seq.Append(matchingPanel.DOScale(Vector3.one, matchingPopDuration).SetEase(matchingPopEase));
        }

        seq.OnComplete(() =>
        {
            NetworkManager.Instance?.StartConnect(playerName);

            // 매칭 화면이 열리면 가짜 카운터 시작
            if (_fakeCounterCoroutine != null) StopCoroutine(_fakeCounterCoroutine);
            _fakeCounterCoroutine = StartCoroutine(FakeMatchingCounter());
        });
    }

    private IEnumerator FakeMatchingCounter()
    {
        int max = NetworkManager.Instance.maxPlayersPerRoom;

        // 자기 자신은 이미 1명
        _displayedCount = Mathf.Max(_displayedCount, 1);
        UpdatePlayerCountUI();

        // max - 1 까지만 채움 (카운트다운 시작 시 max로 점프)
        while (_displayedCount < max - 1)
        {
            float wait = Random.Range(fakeJoinIntervalMin, fakeJoinIntervalMax);
            yield return new WaitForSecondsRealtime(wait);

            _displayedCount++;
            UpdatePlayerCountUI();
            PlayJoinPop(); // 숫자 바뀔 때 팡 연출
        }
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

    // ── 매칭 취소 ─────────────────────────────────────────────

    /// <summary>
    /// 매칭 중 취소 버튼을 눌렀을 때 호출. 네트워크 연결/방 입장을 정리하고
    /// 매칭 UI를 닫은 뒤 모드 선택 화면으로 깔끔하게 되돌린다.
    /// 카운트다운이 시작된 뒤에 눌러도 안전하게 동작한다.
    /// </summary>
    public void OnCancelMatchingClicked()
    {
        // 1. 네트워크 매칭 취소 (연결 중 / 로비 / 방 입장 모든 단계 처리)
        NetworkManager.Instance?.CancelMatching();

        // 2. 진행 중인 연출 코루틴 정지
        if (_fakeCounterCoroutine != null)
        {
            StopCoroutine(_fakeCounterCoroutine);
            _fakeCounterCoroutine = null;
        }
        if (_matchingTextCoroutine != null)
        {
            StopCoroutine(_matchingTextCoroutine);
            _matchingTextCoroutine = null;
        }

        // 3. 내부 상태 초기화
        _countdownStarted = false;
        _displayedCount = 0;

        // 4. 카운트다운 / 게임 시작 텍스트 정리
        if (countdownText != null)
        {
            countdownText.transform.DOKill();
            countdownText.transform.localScale = Vector3.one;
            countdownText.gameObject.SetActive(false);
        }
        if (gameStartText != null)
        {
            gameStartText.transform.DOKill();
            gameStartText.transform.localScale = Vector3.one;
            gameStartText.gameObject.SetActive(false);
        }

        // 5. 매칭 상태 텍스트 위치/내용 복원
        if (matchingStatusText != null)
        {
            matchingStatusText.rectTransform.DOKill();
            matchingStatusText.rectTransform.anchoredPosition = _matchingStatusOriginPos;
            matchingStatusText.text = "매칭 중...";
        }

        // 6. 인원 표시 초기화
        UpdatePlayerCountUI();

        // 7. 매칭 패널 닫기
        if (matchingPanel != null)
        {
            matchingPanel.transform.DOKill();
            matchingPanel.DOScale(Vector3.zero, 0.25f)
                .SetEase(Ease.InBack)
                .SetUpdate(true)
                .OnComplete(() => matchingPanel.gameObject.SetActive(false));
        }

        // 8. 모드 선택 화면 복귀 + 시작 버튼 복원
        if (buttonSelectionPanel != null)
            buttonSelectionPanel?.SetActive(true);
        if (inputPanel != null)
        {
            inputPanel.DOKill();
            inputPanel.DOAnchorPos(_inputPanelOriginPos, 0.3f).SetEase(slideEase).SetUpdate(true);
        }
        if (startButton != null)
            startButton.interactable = true;
    }

    // ── 실제 인원 변경 이벤트 ─────────────────────────────────

    public void UpdatePlayerCount(int realCount)
    {
        // 실제 인원이 가짜 표시보다 많으면 즉시 반영
        // (실제가 가짜보다 적어도 가짜 숫자 유지 → 자연스럽게 보임)
        if (realCount > _displayedCount)
        {
            _displayedCount = realCount;
            UpdatePlayerCountUI();
        }
    }

    private void UpdatePlayerCountUI()
    {
        if (currentPlayerCountText != null)
            currentPlayerCountText.text = $"({_displayedCount} / {NetworkManager.Instance.maxPlayersPerRoom})";
    }

    private void PlayJoinPop()
    {
        if (currentPlayerCountText == null) return;
        currentPlayerCountText.transform.DOKill();
        currentPlayerCountText.transform.localScale = Vector3.one;
        currentPlayerCountText.transform
            .DOPunchScale(Vector3.one * 0.25f, 0.3f, 5)
            .SetUpdate(true);
    }

}