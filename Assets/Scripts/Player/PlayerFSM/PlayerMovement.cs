using UnityEngine;
using UnityEngine.Playables;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{   
    [Header("Player Settings")]
    public float moveSpeed = 6.0f;
    public float rotateSpeed = 10.0f;

    [Header("Physics")]
    public float jumpForce = 7.5f;
    public float originalJumpForce = 10.0f;
    public float gravity = -20.0f;
    public float terminalVelocity = -53.0f;

    [Header("Dash Settings")]
    public float dashSpeed = 50f;
    public float dashDuration = 0.2f;
    public float dashCooldown = 3f;
    [HideInInspector] public float dashCooldownTimer = 0f;

    [Header("Model Settings")]
    public Animator jellyAnimator;
    public UIManager uiManager;

    // 상태 클래스들이 접근할 수 있도록 public + HideInInspector 처리
    [HideInInspector] public CharacterController controller;
    [HideInInspector] public Vector3 inputDir;
    [HideInInspector] public float verticalVelocity;
    [HideInInspector] public Quaternion targetRotation;
    [HideInInspector] public bool isGrounded;

    // 입력 캐싱 (프레임당 1회만 읽기)
    [HideInInspector] public float inputH;
    [HideInInspector] public float inputV;

    // 카메라 벡터
    private Vector3 camForward;
    private Vector3 camRight;

    // 메모리 낭비를 막기 위해 상태들을 미리 생성
    [Header("플레이어 FSM")]
    // 현재 상태
    private PlayerBaseState currentState;

    public PlayerIdleState idleState;
    public PlayerMoveState moveState;
    public PlayerJumpState jumpState;
    public PlayerDashState dashState;
    public PlayerKnockbackState knockbackState;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        jumpForce = originalJumpForce;
        UpdateCameraVectors();

        idleState = new PlayerIdleState(this);
        moveState = new PlayerMoveState(this);
        jumpState = new PlayerJumpState(this);
        dashState = new PlayerDashState(this);
        knockbackState = new PlayerKnockbackState(this);

        // 첫 상태 진입
        ChangeState(idleState);
    }

    void Update()
    {
        UpdateCameraVectors();
        inputH = Input.GetAxis("Horizontal");
        inputV = Input.GetAxis("Vertical");

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        currentState?.Update();
    }

    public bool CanDash()
    {
        return dashCooldownTimer <= 0f
            && currentState != dashState
            && currentState != knockbackState;
    }

    public void ApplyKnockback(Vector3 direction, float force)
    {
        knockbackState.SetKnockback(direction, force);
        ChangeState(knockbackState);
    }

    // 상태 변경 함수
    public void ChangeState(PlayerBaseState newState)
    {
        if (currentState == newState) return;

        currentState?.Exit();
        currentState = newState;
        currentState?.Enter();

        Debug.Log($"[Player] 상태 변경 완료");
    }

    // -----------------------------------------------------------------------
    // 상태 클래스들이 가져다 쓸 공용 도구(Helper)들
    // -----------------------------------------------------------------------
    public void ApplyGravity()
    {
        isGrounded = controller.isGrounded;

        if (isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += gravity * Time.deltaTime;
        if (verticalVelocity < terminalVelocity) verticalVelocity = terminalVelocity;
    }

    public void CalculateMoveDirection()
    {
        inputDir = (camForward * inputV + camRight * inputH).normalized;
    }

    public bool IsMoveInputActive()
    {
        return (inputH * inputH + inputV * inputV) > 0.001f;
    }

    public void MoveAndRotate()
    {
        Vector3 finalMove = inputDir * moveSpeed;
        finalMove.y = verticalVelocity;
        controller.Move(finalMove * Time.deltaTime);

        if (inputDir != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(inputDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        }
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

    public void OnFailAnimationFinished()
    {
        uiManager.SetState(UIState.GameOver);
    }
}