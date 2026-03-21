using UnityEngine;
using UnityEngine.Playables;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{   
    [Header("Player Settings")]
    public float moveSpeed = 6.0f;
    public float rotateSpeed = 10.0f;

    [Header("Physics")]
    public float jumpForce = 7.5f;
    public float originalJumpForce = 10.0f;
    public float gravity = -20.0f;
    public float terminalVelocity = -53.0f;

    [Header("Model Settings")]
    public Animator jellyAnimator;
    public UIManager uiManager;

    // 상태 클래스들이 접근할 수 있도록 public + HideInInspector 처리
    [HideInInspector] public CharacterController controller;
    [HideInInspector] public Vector3 inputDir;
    [HideInInspector] public float verticalVelocity;
    [HideInInspector] public Quaternion targetRotation;
    [HideInInspector] public bool isGrounded;

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

    void Start()
    {
        controller = GetComponent<CharacterController>();
        jumpForce = originalJumpForce;
        UpdateCameraVectors();

        // 상태들 초기화
        idleState = new PlayerIdleState(this);
        moveState = new PlayerMoveState(this);
        jumpState = new PlayerJumpState(this);

        // 첫 상태 진입
        ChangeState(idleState);
    }

    void Update()
    {
        UpdateCameraVectors();

        // 현재 상태의 Update
        currentState?.Update();
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
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        inputDir = (camForward * v + camRight * h).normalized;
    }

    public bool IsMoveInputActive()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        return (h * h + v * v) > 0.001f;
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