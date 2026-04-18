using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI; // UI 관련 기능을 위해 추가
using System.Collections;

public class ClearJudge : MonoBehaviour
{
    public ResultStarsUI resultStarsUI;
    public GameTimer gameTimer;
    public UIManager uiManager;
    public UIPoolManager uIPoolManager;
    public PlayerController playerController;

    [Header("연출용 UI 연결")]
    [Tooltip("화면 전체를 덮는 검은색 패널의 CanvasGroup (페이드 효과용)")]
    public CanvasGroup fadeCanvasGroup;
    [Tooltip("보여줄 에필로그 UI 오브젝트")]
    public GameObject epilogueUI;
    [Tooltip("페이드 효과 걸리는 시간")]
    public float fadeDuration = 1.0f;

    public float halfLength = 6.0f;
    public LayerMask playerLayerMask;

    // ★ 중복 실행 방지용 플래그 변수
    private bool isCleared = false;

    [Header("저울 설정")]
    [Tooltip("밟았을 때 아래로 내려갈 거리")]
    public float sinkDistance = 1.0f;

    [Tooltip("내려가고 올라오는 속도 (낮을수록 더 서서히 움직임)")]
    public float moveSpeed = 1.5f;

    public Transform scaleTransform;

    private Vector3 _originalPos;
    private Vector3 _pressedPos;
    private Vector3 _targetPos;

    // 저울 위에 올라가 있는 물체의 개수
    private int _objectsOnScale = 0;

    void Start()
    {
        _originalPos = scaleTransform.position;
        _pressedPos = _originalPos + Vector3.down * sinkDistance;
        _targetPos = _originalPos;

        if (epilogueUI != null) epilogueUI.SetActive(false);
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false; // 평소에는 클릭 방해하지 않도록
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("PlayerMesh"))
        {
            _objectsOnScale++;
            _targetPos = _pressedPos;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("PlayerMesh"))
        {
            _objectsOnScale--;

            if (_objectsOnScale <= 0)
            {
                _objectsOnScale = 0;
                _targetPos = _originalPos;
            }
        }
    }

    private void Update()
    {
        // ★ 클리어 이후에는 저울도 멈추고(고정) 더 이상 로직을 실행하지 않음
        if (isCleared) return;

        // 1. 저울 서서히 이동
        scaleTransform.position = Vector3.MoveTowards(scaleTransform.position, _targetPos, moveSpeed * Time.deltaTime);


        // 수정 후 (약 0.01f 이내로 들어오면 도착한 것으로 간주)
        if (Vector3.Distance(scaleTransform.position, _pressedPos) < 0.01f && _objectsOnScale > 0)
        {
            Debug.Log("저울 눌림");
            //JudgeClear();
        }
    }

    //private void JudgeClear()
    //{
    //    // 조건 체크: 스케일 레벨 달성 + 현재 목표 색상 달성 여부
    //    bool isConditionMet = DataManager.Instance.playerCurrentScaleLevel >= DataManager.Instance.targetScaleLevel
    //                          && GameModeManager.Instance != null && GameModeManager.Instance.IsTargetAchieved;

    //    if (isConditionMet)
    //    {
    //        isCleared = true; // 중복 실행 방지

    //        // 걷는 소리 즉시 중지 및 컨트롤 비활성화
    //        PlaySFXAudio.Instance.StopWalking();
    //        playerController.enabled = false;

    //        // ★ 연출 코루틴 시작
    //        StartCoroutine(ClearSequence());
    //    }
    //}

    // ★ 클리어 연출 시퀀스 (페이드 -> 에필로그 -> 페이드 -> 결과창)
    private IEnumerator ClearSequence()
    {
        // 1. 화면 페이드 아웃 (화면이 검어짐: Alpha 0 -> 1)
        yield return StartCoroutine(FadeRoutine(0f, 1f));

        // 2. 에필로그 화면 켜기 (아직 화면은 검은 상태)
        if (epilogueUI != null) epilogueUI.SetActive(true);

        // 3. 화면 페이드 인 (에필로그가 서서히 보임: Alpha 1 -> 0)
        yield return StartCoroutine(FadeRoutine(1f, 0f));

        // 4. 클릭 대기 (유저가 화면을 클릭할 때까지 대기)
        // PC는 마우스 클릭(0), 모바일은 터치 대응을 위해 GetMouseButtonDown(0) 사용
        yield return new WaitUntil(() => Input.GetMouseButtonDown(0));

        // 5. 화면 페이드 아웃 (다시 화면이 검어짐: Alpha 0 -> 1)
        yield return StartCoroutine(FadeRoutine(0f, 1f));

        // 6. 에필로그 끄고 미션 완료 사운드 재생
        if (epilogueUI != null) epilogueUI.SetActive(false);

        // 7. 별점 계산 및 UI 상태 변경 (결과 화면 준비)
        if (gameTimer.limitTime - gameTimer.currentTime <= DataManager.Instance.targetTime)
        {
            resultStarsUI.SetStarIndex(3);
        }
        else
        {
            resultStarsUI.SetStarIndex(2);
        }

        uIPoolManager.DisableParent();
        uiManager.SetState(UIState.GameOver);

        // 8. 화면 페이드 인 (결과 화면이 서서히 보임: Alpha 1 -> 0)
        yield return StartCoroutine(FadeRoutine(1f, 0f));
        PlaySFXAudio.Instance.PlayMissionCompleteSound();
    }

    // 투명도 조절을 위한 헬퍼 코루틴
    private IEnumerator FadeRoutine(float startAlpha, float endAlpha)
    {
        if (fadeCanvasGroup == null) yield break;

        fadeCanvasGroup.blocksRaycasts = true; // 페이드 중 터치 방지
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime; // 게임 시간이 멈춰도 UI 연출은 되도록 unscaled 사용
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, timer / fadeDuration);
            yield return null;
        }

        fadeCanvasGroup.alpha = endAlpha;

        // 페이드가 완전히 끝나서 화면이 다 보일 때(Alpha 0)만 터치 허용
        if (endAlpha == 0f)
        {
            fadeCanvasGroup.blocksRaycasts = false;
        }
    }
}