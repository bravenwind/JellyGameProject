using UnityEngine;
using JellyNet;

// ★ 이 캐릭터의 Rigidbody는 물리로 움직이기 위한 것이 아니다
//   CharacterController는 Rigidbody 물리를 <b>완전히 무시한다.</b> 그런데도 프리팹에
//   kinematic Rigidbody가 붙어 있는 이유는 두 가지다.
//     ① 트리거 콜백 — 두 콜라이더가 다 Rigidbody 없이 정적이면 유니티가 OnTrigger를 안 보낸다
//     ② ChocolateFluid 등이 other.attachedRigidbody 로 대상을 잡는다
//   즉 '움직이는 물체'라는 표시이자 수신 장치다. 지우면 흡수·초콜릿·밀크가 조용히 죽는다.
//
//   탈락해서 떨어질 때만 이 Rigidbody가 실제로 물리에 참여한다
//   (LanPlayerState.BeginPhysicsFall → PhysicsFall.Begin).
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{   
    [Header("Player Settings")]
    [SerializeField] private float moveSpeed = 6.0f;
    public float MoveSpeed { get { return moveSpeed; } set { moveSpeed = value; } }
    [SerializeField] private float rotateSpeed = 10.0f;

    [Header("Physics")]
    // ★ 인스펙터에 내보내지 않는다 — Start에서 originalJumpForce로 무조건 덮어쓴다
    //   조절할 값은 아래 originalJumpForce 하나뿐이고, 이건 그로부터 파생되는 현재값이다
    private float jumpForce;
    public float JumpForce { get { return jumpForce; } set { jumpForce = value; } }
    [SerializeField] private float originalJumpForce = 10.0f;
    public float OriginalJumpForce { get { return originalJumpForce; } }
    [SerializeField] private float gravity = -20.0f;
    [SerializeField] private float terminalVelocity = -53.0f;

    [Header("Dash Settings")]
    [SerializeField] private float dashSpeed = 50f;
    public float DashSpeed { get { return dashSpeed; } }
    [SerializeField] private float dashDuration = 0.2f;
    public float DashDuration { get { return dashDuration; } }
    [SerializeField] private float dashCooldown = 3f;
    public float DashCooldown { get { return dashCooldown; } }
    //쿨타임 잔여 시간. 밖에서는 읽기만 하고, 거는 것은 아래 두 메서드로만 한다 —
    //예전엔 public 필드라 어디서든 임의의 값을 써넣을 수 있었다
    public float DashCooldownTimer { get; private set; }
    public float AttackCooldownTimer { get; private set; }

    /// <summary>HUD가 쿨타임 하나를 그리는 데 필요한 전부. 세 값을 따로 묻지 않게 묶는다.</summary>
    public readonly struct Cooldown
    {
        public readonly float Ratio;      // 0 = 준비 완료, 1 = 방금 써서 풀 쿨다운
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

    // ── 로컬 플레이어(내 캐릭터) 전역 접근점 — HUD(대쉬 쿨타임 UI 등)에서 사용 ──
    // LanPlayerSetup이 IsMine일 때 MarkAsLocal()로 지정한다.
    public static PlayerMovement Local { get; private set; }
    public void MarkAsLocal() => Local = this;

    // 게임 시작 카운트다운(3-2-1) 동안 로컬 입력을 잠근다(이동/대쉬/공격/점프 차단).
    // 플레이어는 Idle 상태로 살아있어 Idle 애니메이션은 계속 재생된다. 로컬만 입력을 읽으므로 static로 충분.
    public static bool InputLocked = false;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInputLocked() => InputLocked = false;


    [Header("Model Settings")]
    [SerializeField] private Animator animator;
    public Animator Anim { get { return animator; } }

    [Header("Bat (Push Mode)")]
    [Tooltip("배트 오브젝트의 Transform (플레이어 자식으로 배치)")]
    [SerializeField] private Transform batPivot;
    public Transform BatPivot { get { return batPivot; } }
    [Tooltip("평상시 배트 숨기기")]
    [SerializeField] private bool hideBatWhenIdle = true;
    public bool HideBatWhenIdle { get { return hideBatWhenIdle; } }

    // ─────────────────────────────────────────────────────────
    //  상태 클래스들이 함께 쓰는 값
    // ─────────────────────────────────────────────────────────
    //
    // ★ 예전엔 [HideInInspector] public 필드였다
    //   그 표기는 "인스펙터에는 감추되 직렬화는 한다"는 뜻이라, 매 프레임 바뀌는
    //   런타임 값이 씬 파일에 저장되고 다음 실행에 옛 값으로 되살아난다.
    //   감추고 싶었던 것이지 저장하고 싶었던 게 아니므로 프로퍼티가 맞다.
    public CharacterController Controller { get; private set; }
    public Vector3 InputDir { get; set; }
    public float VerticalVelocity { get; set; }
    public bool IsGrounded { get; private set; }


    // 입력 캐싱 (프레임당 1회만 읽기)
    private float inputH;
    private float inputV;

    // 카메라 벡터
    private Vector3 camForward;
    private Vector3 camRight;

    // 메모리 낭비를 막기 위해 상태들을 미리 생성
    [Header("플레이어 FSM")]
    // 현재 상태
    private PlayerBaseState currentState;

    //Start에서 한 번 만들어 재사용한다. 인스펙터 값이 아니라 런타임 객체다
    public PlayerIdleState IdleState { get; private set; }
    public PlayerMoveState MoveState { get; private set; }
    public PlayerJumpState JumpState { get; private set; }
    public PlayerDashState DashState { get; private set; }
    private PlayerKnockbackState knockbackState;
    public PlayerAttackState AttackState { get; private set; }

    /// <summary>애니메이션 트리거를 네트워크로 알리는 창구. 루트에 붙어 있다.</summary>
    public LanPlayerVisual Visual { get; private set; }

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

        // 첫 상태 진입
        ChangeState(IdleState);
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

    public void ApplyKnockback(Vector3 direction, float force)
    {
        knockbackState.SetKnockback(direction, force);
        ChangeState(knockbackState);
    }

    // 상태 변경 함수
    public void ChangeState(PlayerBaseState newState)
    {
        if (currentState == newState)
            return;

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
        IsGrounded = Controller.isGrounded;

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
        // ★ 컨트롤러가 꺼져 있으면 움직이지 않는다.
        //   흡수 연출·발판 낙하·탈락 처리가 CharacterController를 끄는데,
        //   FSM은 그걸 모르고 계속 Move를 부른다 → 매 프레임 에러가 쏟아진다.
        //   끄는 쪽마다 FSM까지 챙기게 하는 것보다, 쓰는 쪽에서 한 번 막는 게 확실하다.
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