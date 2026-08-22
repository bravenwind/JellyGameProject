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
    // LanPlayerSetup이 IsMine일 때 MarkAsLocal()로 지정한다.
    public static PlayerMovement Local { get; private set; }
    public void MarkAsLocal() => Local = this;

    // 게임 시작 카운트다운(3-2-1) 동안 로컬 입력을 잠근다(이동/대쉬/공격/점프 차단).
    // 플레이어는 Idle 상태로 살아있어 Idle 애니메이션은 계속 재생된다. 로컬만 입력을 읽으므로 static로 충분.
    public static bool InputLocked = false;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInputLocked() => InputLocked = false;

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
    public Animator animator;
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

    /// <summary>애니메이션 트리거를 네트워크로 알리는 창구. 루트에 붙어 있다.</summary>
    public JellyNet.LanPlayerVisual Visual { get; private set; }

    /// <summary>
    /// 판정에 쓰는 내 크기. 사거리·흡수 판정은 전부 이 값을 봐야 한다.
    /// transform.localScale.x는 '지금 보이는 크기'라 커지는 연출 도중 호스트 재검증과 어긋난다.
    /// </summary>
    public float AuthorityScale
    {
        get { return Visual != null ? Visual.ScaleValue : transform.localScale.x; }
    }

    //Awake에서 잡는다. 원격 아바타는 스폰 도중 이 컴포넌트가 꺼지므로 Start가 아예 안 불린다
    void Awake()
    {
        Visual = GetComponentInParent<JellyNet.LanPlayerVisual>();
    }

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
        if (InputLocked)
        {
            inputH = 0f;
            inputV = 0f;
        }
        else
        {
            inputH = Input.GetAxis("Horizontal");
            inputV = Input.GetAxis("Vertical");
        }

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
        return !InputLocked
            && dashCooldownTimer <= 0f
            && currentState != dashState
            && currentState != knockbackState;
    }

    public bool CanAttack()
    {
        return !InputLocked
            && GameState.CurrentGameMode == GameModeType.Push
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

#if UNITY_EDITOR
        // [N1/U7] 상태 전환 로그는 에디터 전용 + 어느 상태인지 포함 (빌드 로그 스파이크 방지)
        Debug.Log($"[Player] 상태 변경 → {newState?.GetType().Name}");
#endif
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
        // ★ 컨트롤러가 꺼져 있으면 움직이지 않는다.
        //   흡수 연출·발판 낙하·탈락 처리가 CharacterController를 끄는데,
        //   FSM은 그걸 모르고 계속 Move를 부른다 → 매 프레임 에러가 쏟아진다.
        //   끄는 쪽마다 FSM까지 챙기게 하는 것보다, 쓰는 쪽에서 한 번 막는 게 확실하다.
        if (controller == null || !controller.enabled) return;

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