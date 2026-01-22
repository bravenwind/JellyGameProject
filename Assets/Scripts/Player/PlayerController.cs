using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

// 필수 컴포넌트 변경
[RequireComponent(typeof(CharacterController))]
public class PlayerController : BaseFSM
{
    [Header("Player Settings")]
    public float moveSpeed = 6.0f;
    public float rotateSpeed = 10.0f;

    [Header("Physics")]
    public float jumpForce = 7.5f;
    public float gravity = -20.0f; // CharacterController는 중력을 직접 설정해야 함 (보통 -9.81보다 크게 줌)
    public float terminalVelocity = -53.0f; // 낙하 최대 속도 제한

    [Header("Model Settings")]
    public Animator jellyAnimator;

    // 컴포넌트 캐싱
    private CharacterController controller;
    private Vector3 camForward;
    private Vector3 camRight;

    // 내부 변수
    private Vector3 inputDir;
    private float verticalVelocity; // Y축 속도 (점프/중력)
    private Quaternion targetRotation;

    // 상태 확인용
    public bool isGrounded;

    protected override void Start()
    {
        base.Start();

        controller = GetComponent<CharacterController>();

        // 씬 별 속도 설정 (기존 로직 유지)
        if (SceneManager.GetActiveScene() == SceneManager.GetSceneByName("TileScene") ||
            SceneManager.GetActiveScene() == SceneManager.GetSceneByName("ResourceApplyScene"))
        {
            moveSpeed = 6.0f;
        }

        UpdateCameraVectors();
    }

    protected override void Update()
    {
        base.Update(); // FSM의 상태별 Update 실행
        UpdateCameraVectors();

        // CharacterController는 FixedUpdate가 아닌 Update에서 움직이는 것이 부드러움
        // 다만 FSM 구조상 각 상태(Update_Move 등)에서 Move를 호출하도록 함
    }

    // CharacterController는 물리 연산이 아니므로 FixedUpdate를 사용하지 않음
    protected override void FixedUpdate()
    {
        // 비워둠 (BaseFSM 때문에 남겨둠)
    }

    // 매 프레임 중력 적용 로직 (공통)
    private void ApplyGravity()
    {
        // CharacterController의 isGrounded는 Move()가 호출된 직후 갱신됨
        isGrounded = controller.isGrounded;

        if (isGrounded && verticalVelocity < 0)
        {
            // 땅에 붙어있을 때도 약간의 힘을 주어 바닥 판정을 확실하게 함 (-2f)
            verticalVelocity = -2f;
        }

        // 중력 가속도 누적
        verticalVelocity += gravity * Time.deltaTime;

        // 낙하 속도 제한
        if (verticalVelocity < terminalVelocity)
        {
            verticalVelocity = terminalVelocity;
        }
    }

    // -----------------------------------------------------------------------
    // 1. IDLE 상태
    // -----------------------------------------------------------------------
    protected override void Enter_Idle()
    {
        Debug.Log("[Player] 대기 상태 진입.");
        inputDir = Vector3.zero;
        verticalVelocity = 0; // 초기화
    }

    protected override void Update_Idle()
    {
        ApplyGravity();

        // 제자리에서도 중력 적용을 위해 Move 호출 (안하면 공중에 떠있을 수 있음)
        controller.Move(new Vector3(0, verticalVelocity, 0) * Time.deltaTime);

        // 점프
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            ChangeState(UnitState.Jump);
            return;
        }

        // 이동 입력 감지
        if (IsMoveInputActive())
        {
            ChangeState(UnitState.Move);
            return;
        }
    }

    // -----------------------------------------------------------------------
    // 2. MOVE 상태
    // -----------------------------------------------------------------------
    protected override void Enter_Move()
    {
        base.Enter_Move();
        if (jellyAnimator != null) jellyAnimator.SetBool("IsMoving", true);

        if (PlayFXAudio.Instance != null)
            PlayFXAudio.Instance.StartWalking();
    }

    protected override void Update_Move()
    {
        // 1. 상태 전환 체크
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            ChangeState(UnitState.Jump);
            return;
        }

        if (!IsMoveInputActive())
        {
            ChangeState(UnitState.Idle);
            return;
        }

        // 2. 이동 및 회전 계산
        CalculateMoveDirection(); // inputDir 갱신
        ApplyGravity();           // verticalVelocity 갱신

        // 3. 최종 이동 적용 (XZ 이동 + Y 중력)
        Vector3 finalMove = inputDir * moveSpeed;
        finalMove.y = verticalVelocity;

        controller.Move(finalMove * Time.deltaTime); // ⭐ 핵심 이동 함수

        // 4. 회전 적용
        if (inputDir != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(inputDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        }
    }

    protected override void Exit_Move()
    {
        base.Exit_Move();
        if (jellyAnimator != null) jellyAnimator.SetBool("IsMoving", false);

        if (PlayFXAudio.Instance != null)
            PlayFXAudio.Instance.StopWalking();
    }

    // -----------------------------------------------------------------------
    // 3. JUMP 상태
    // -----------------------------------------------------------------------
    protected override void Enter_Jump()
    {
        if (jellyAnimator != null) jellyAnimator.SetTrigger("Jump");
        PlayFXAudio.Instance.PlayJumpSound();

        // 즉시 위쪽 속도 부여 (v = sqrt(h * -2 * g) 공식 대신 단순 힘 적용)
        verticalVelocity = jumpForce;
    }

    protected override void Update_Jump()
    {
        // 공중에서도 이동 가능 (Air Control)
        CalculateMoveDirection();
        ApplyGravity();

        Vector3 finalMove = inputDir * moveSpeed;
        finalMove.y = verticalVelocity;

        controller.Move(finalMove * Time.deltaTime);

        // 회전
        if (inputDir != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(inputDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        }

        // 착지 체크: 상승 중이 아니고(내려오는 중), 땅에 닿았으면 Idle로
        if (verticalVelocity < 0 && controller.isGrounded)
        {
            ChangeState(UnitState.Idle);
        }
    }

    // -----------------------------------------------------------------------
    // Helper Methods
    // -----------------------------------------------------------------------

    private void CalculateMoveDirection()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // 카메라 기준 방향 계산
        inputDir = (camForward * v + camRight * h).normalized;
    }

    private bool IsMoveInputActive()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        return (h * h + v * v) > 0.001f; // sqrMagnitude 대용
    }

    private void UpdateCameraVectors()
    {
        if (Camera.main == null) return;

        Transform cam = Camera.main.transform;
        camForward = cam.forward;
        camRight = cam.right;

        camForward.y = 0f;
        camRight.y = 0f;

        camForward.Normalize();
        camRight.Normalize();
    }
}