using UnityEngine;
using JellyNet;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6.0f;

    [SerializeField] private float rotateSpeed = 10.0f;

    [SerializeField] private float originalJumpForce = 10.0f;

    [SerializeField] private float gravity = -20.0f;
    [SerializeField] private float terminalVelocity = -53.0f;

    [SerializeField] private float dashSpeed = 50f;

    [SerializeField] private float dashDuration = 0.2f;

    [SerializeField] private float dashCooldown = 3f;

    [SerializeField] private KeyCode attackKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;

    [SerializeField] private float inputBufferTime = 0.15f;

    [SerializeField] private Animator animator;

    [SerializeField] private Transform batPivot;

    [SerializeField] private bool hideBatWhenIdle = true;

    public static PlayerMovement Local { get; private set; }

    public static bool InputLocked = false;

    private float jumpForce;

    public float DashCooldownTimer { get; private set; }
    public float AttackCooldownTimer { get; private set; }

    public CharacterController Controller { get; private set; }
    public Vector3 InputDir { get; set; }
    public float VerticalVelocity { get; set; }

    public bool IsGrounded => Controller != null && Controller.isGrounded;

    private float inputH;
    private float inputV;

    private BufferedPress attackPress;
    private BufferedPress dashPress;
    private BufferedPress jumpPress;

    private struct BufferedPress
    {
        private float pressedAt;
        private bool pending;

        public void Record(float now)
        {
            pressedAt = now;
            pending = true;
        }

        public void Clear() => pending = false;

        public bool TryConsume(float now, float window)
        {
            if (!pending || now - pressedAt > window)
                return false;

            pending = false;
            return true;
        }
    }

    private Vector3 camForward;
    private Vector3 camRight;

    public PlayerIdleState IdleState { get; private set; }
    public PlayerMoveState MoveState { get; private set; }
    public PlayerJumpState JumpState { get; private set; }
    public PlayerDashState DashState { get; private set; }
    private PlayerKnockbackState knockbackState;
    public PlayerAttackState AttackState { get; private set; }

    public LanPlayerVisual Visual { get; private set; }

    private PlayerBaseState currentState;

    public float MoveSpeed { get { return moveSpeed; } set { moveSpeed = value; } }
    public float JumpForce { get { return jumpForce; } set { jumpForce = value; } }
    public float OriginalJumpForce { get { return originalJumpForce; } }
    public float DashSpeed { get { return dashSpeed; } }
    public float DashDuration { get { return dashDuration; } }

    public readonly struct Cooldown
    {
        public readonly float Ratio;      // 0 = 준비 완료, 1 = 방금 사용
        public readonly float Remaining;  // 남은 초
        public readonly bool Ready;

        public Cooldown(float remaining, float max)
        {
            Remaining = remaining;
            Ratio = max > 0f ? Mathf.Clamp01(remaining / max) : 0f;
            Ready = remaining <= 0f;
        }
    }

    public Cooldown DashCooldownInfo => new Cooldown(DashCooldownTimer, dashCooldown);

    public Cooldown AttackCooldownInfo
    {
        get
        {
            DataManager dm = DataManager.Instance;
            return new Cooldown(AttackCooldownTimer, dm != null ? dm.BatCooldown : 0f);
        }
    }

    public void StartDashCooldown() => DashCooldownTimer = dashCooldown;
    public void StartAttackCooldown()
    {
        DataManager dm = DataManager.Instance;
        AttackCooldownTimer = dm != null ? dm.BatCooldown : 0f;
    }
    public void MarkAsLocal() => Local = this;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInputLocked() => InputLocked = false;
    public Animator Anim { get { return animator; } }
    public Transform BatPivot { get { return batPivot; } }
    public bool HideBatWhenIdle { get { return hideBatWhenIdle; } }

    public float AuthorityScale
    {
        get { return Visual != null ? Visual.ScaleValue : transform.localScale.x; }
    }

    void Awake()
    {
        Visual = GetComponentInParent<LanPlayerVisual>();
    }

    void Start()
    {
        Controller = GetComponent<CharacterController>();
        jumpForce = originalJumpForce;
        UpdateCameraVectors();

        IdleState = new PlayerIdleState(this);
        MoveState = new PlayerMoveState(this);
        JumpState = new PlayerJumpState(this);
        DashState = new PlayerDashState(this);
        knockbackState = new PlayerKnockbackState(this);
        AttackState = new PlayerAttackState(this);

        ChangeState(IdleState);
    }

    void Update()
    {
        UpdateCameraVectors();
        if (InputLocked)
        {
            inputH = 0f;
            inputV = 0f;

            attackPress.Clear();
            dashPress.Clear();
            jumpPress.Clear();
        }
        else
        {
            inputH = Input.GetAxis("Horizontal");
            inputV = Input.GetAxis("Vertical");

            if (Input.GetKeyDown(attackKey)) attackPress.Record(Time.time);
            if (Input.GetKeyDown(dashKey)) dashPress.Record(Time.time);
            if (Input.GetKeyDown(jumpKey)) jumpPress.Record(Time.time);
        }

        if (DashCooldownTimer > 0f)
            DashCooldownTimer -= Time.deltaTime;
        if (AttackCooldownTimer > 0f)
            AttackCooldownTimer -= Time.deltaTime;

        currentState?.Update();
    }

    private void OnDestroy()
    {
        if (Local == this)
            Local = null;
    }

    public bool CanDash()
    {
        return !InputLocked
            && DashCooldownTimer <= 0f
            && currentState != DashState
            && currentState != knockbackState;
    }

    public bool CanAttack()
    {
        return !InputLocked
            && GameState.CurrentGameMode == GameModeType.Push
            && AttackCooldownTimer <= 0f
            && currentState != AttackState
            && currentState != knockbackState;
    }

    public bool CanJump()
    {
        return !InputLocked
            && IsGrounded
            && currentState != JumpState
            && currentState != knockbackState;
    }

    public bool TryStartAction()
    {
        float now = Time.time;

        if (CanAttack() && attackPress.TryConsume(now, inputBufferTime))
        {
            ChangeState(AttackState);
            return true;
        }

        if (CanDash() && dashPress.TryConsume(now, inputBufferTime))
        {
            ChangeState(DashState);
            return true;
        }

        if (CanJump() && jumpPress.TryConsume(now, inputBufferTime))
        {
            ChangeState(JumpState);
            return true;
        }

        return false;
    }

    public void FinishAction()
    {
        if (TryStartAction())
            return;

        ChangeState(IsMoveInputActive() ? MoveState : IdleState);
    }

    public void ApplyKnockback(Vector3 direction, float force)
    {
        knockbackState.SetKnockback(direction, force);
        ChangeState(knockbackState);
    }

    public void ChangeState(PlayerBaseState newState)
    {
        if (currentState == newState)
            return;

        currentState?.Exit();
        currentState = newState;
        currentState?.Enter();
    }

    public void ApplyGravity()
    {
        if (IsGrounded && VerticalVelocity < 0)
            VerticalVelocity = -2f;

        VerticalVelocity += gravity * Time.deltaTime;
        if (VerticalVelocity < terminalVelocity)
            VerticalVelocity = terminalVelocity;
    }

    public void CalculateMoveDirection()
    {
        InputDir = (camForward * inputV + camRight * inputH).normalized;
    }

    public bool IsMoveInputActive()
    {
        return (inputH * inputH + inputV * inputV) > 0.001f;
    }

    public void MoveAndRotate()
    {
        if (Controller == null || !Controller.enabled)
            return;

        Vector3 finalMove = InputDir * moveSpeed;
        finalMove.y = VerticalVelocity;
        Controller.Move(finalMove * Time.deltaTime);

        if (InputDir != Vector3.zero)
        {
            transform.rotation = SmoothDamping.RotateTowards(
                transform.rotation, InputDir, rotateSpeed, Time.deltaTime);
        }
    }

    private void UpdateCameraVectors()
    {
        if (Camera.main == null)
            return;
        Transform cam = Camera.main.transform;
        camForward = cam.forward;
        camRight = cam.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();
    }
}
