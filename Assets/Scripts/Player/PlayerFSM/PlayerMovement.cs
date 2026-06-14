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
    [HideInInspector] public float attackCooldownTimer = 0f;

    // ── 로컬 플레이어(내 캐릭터) 전역 접근점 — HUD(대쉬 쿨타임 UI 등)에서 사용 ──
    // NetworkPlayerSync.SetupLocalPlayer(photonView.IsMine)에서 MarkAsLocal()로 지정한다.
    public static PlayerMovement Local { get; private set; }
    public void MarkAsLocal() => Local = this;

    /// <summary>0 = 대쉬 준비 완료, 1 = 방금 써서 풀 쿨다운. (UI 채움 비율용)</summary>
    public float DashCooldownRatio => dashCooldown > 0f ? Mathf.Clamp01(dashCooldownTimer / dashCooldown) : 0f;
    /// <summary>쿨타임이 끝난 상태(상태머신 제약까지 보려면 CanDash() 사용).</summary>
    public bool DashReady => dashCooldownTimer <= 0f;

    /// <summary>0 = 공격 준비 완료, 1 = 방금 써서 풀 쿨다운. 최댓값은 DataManager.batCooldown(Push 전용).</summary>
    public float AttackCooldownRatio
    {
        get
        {
            var dm = DataManager.Instance;
            float max = dm != null ? dm.batCooldown : 0f;
            return max > 0f ? Mathf.Clamp01(attackCooldownTimer / max) : 0f;
        }
    }
    /// <summary>공격 쿨타임이 끝난 상태(모드/상태머신 제약까지 보려면 CanAttack() 사용).</summary>
    public bool AttackReady => attackCooldownTimer <= 0f;

    [Header("Model Settings")]
    public Animator jellyAnimator;
    public UIManager uiManager;

    [Header("Bat (Push Mode)")]
    [Tooltip("배트 오브젝트의 Transform (플레이어 자식으로 배치)")]
    public Transform batPivot;
    [Tooltip("평상시 배트 숨기기")]
    public bool hideBatWhenIdle = true;

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
    public PlayerAttackState attackState;

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
        attackState = new PlayerAttackState(this);

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
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;

        currentState?.Update();
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    public bool CanDash()
    {
        return dashCooldownTimer <= 0f
            && currentState != dashState
            && currentState != knockbackState;
    }

    public bool CanAttack()
    {
        return GameState.CurrentGameMode == GameModeType.Push
            && attackCooldownTimer <= 0f
            && currentState != attackState
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