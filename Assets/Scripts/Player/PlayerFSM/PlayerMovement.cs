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
    #region Player Settings

    [Header("Player Settings")]
    [SerializeField] private float moveSpeed = 6.0f;

    [SerializeField] private float rotateSpeed = 10.0f;

    #endregion

    #region Physics

    [Header("Physics")]
    // ★ 인스펙터에 내보내지 않는다 — Start에서 originalJumpForce로 무조건 덮어쓴다
    //   조절할 값은 아래 originalJumpForce 하나뿐이고, 이건 그로부터 파생되는 현재값이다
    private float jumpForce;

    [SerializeField] private float originalJumpForce = 10.0f;

    [SerializeField] private float gravity = -20.0f;
    [SerializeField] private float terminalVelocity = -53.0f;

    #endregion

    #region Dash Settings

    [Header("Dash Settings")]
    [SerializeField] private float dashSpeed = 50f;

    [SerializeField] private float dashDuration = 0.2f;

    [SerializeField] private float dashCooldown = 3f;

    #endregion

    #region Input Settings

    [Header("Input Settings")]
    [SerializeField] private KeyCode attackKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;

    [Tooltip("누른 입력을 이 시간(초) 동안 기억한다. 대쉬·공격이 끝나기 직전에 누른 키가 버려지지 않게 한다")]
    [SerializeField] private float inputBufferTime = 0.15f;

    #endregion

    #region 상태

    //쿨타임 잔여 시간. 밖에서는 읽기만 하고, 거는 것은 아래 두 메서드로만 한다 —
    //예전엔 public 필드라 어디서든 임의의 값을 써넣을 수 있었다
    public float DashCooldownTimer { get; private set; }
    public float AttackCooldownTimer { get; private set; }

    // ── 로컬 플레이어(내 캐릭터) 전역 접근점 — HUD(대쉬 쿨타임 UI 등)에서 사용 ──
    // LanPlayerSetup이 IsMine일 때 MarkAsLocal()로 지정한다.
    public static PlayerMovement Local { get; private set; }

    #endregion

    #region 정적 필드

    // 게임 시작 카운트다운(3-2-1) 동안 로컬 입력을 잠근다(이동/대쉬/공격/점프 차단).
    // 플레이어는 Idle 상태로 살아있어 Idle 애니메이션은 계속 재생된다. 로컬만 입력을 읽으므로 static로 충분.
    public static bool InputLocked = false;

    #endregion

    #region Model Settings

    [Header("Model Settings")]
    [SerializeField] private Animator animator;

    #endregion

    #region Bat (Push Mode)

    [Header("Bat (Push Mode)")]
    [Tooltip("배트 오브젝트의 Transform (플레이어 자식으로 배치)")]
    [SerializeField] private Transform batPivot;

    [Tooltip("평상시 배트 숨기기")]
    [SerializeField] private bool hideBatWhenIdle = true;

    #endregion

    #region 상태 클래스들이 함께 쓰는 값

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

    // ★ 땅 판정은 컨트롤러 한 곳에서만 읽는다
    //   예전엔 ApplyGravity가 isGrounded를 이 프로퍼티에 옮겨 적었고, Jump만 컨트롤러를
    //   직접 읽었다. 옮겨 적은 값은 그 뒤에 Move가 돌면 한 프레임 늦은 값이 되어
    //   두 판정이 서로 다른 답을 낼 수 있었다. 사본을 없애고 원본을 바로 읽는다.
    public bool IsGrounded => Controller != null && Controller.isGrounded;

    // 입력 캐싱 (프레임당 1회만 읽기)
    private float inputH;
    private float inputV;

    // ★ 버튼 입력은 여기서만 읽고, 상태들은 "눌렸는가"를 꺼내 쓰기만 한다
    //   예전엔 Idle·Move가 Input.GetKeyDown을 직접 읽었다. 그래서 Dash·Attack 도중에
    //   누른 키는 읽는 상태가 없어 그대로 사라졌고, 같은 키 검사가 두 상태에 두 벌 있었다.
    //   이제 누른 순간을 inputBufferTime 동안 기억해 두었다가, 행동을 시작할 수 있게 되면 꺼낸다.
    private BufferedPress attackPress;
    private BufferedPress dashPress;
    private BufferedPress jumpPress;

    /// <summary>버튼 하나의 "눌렀다"를 잠시 기억해 두는 칸.</summary>
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

        /// <summary>기억 시간 안에 눌린 입력이 있으면 꺼내면서 true. 한 번 누른 건 한 번만 쓰인다.</summary>
        public bool TryConsume(float now, float window)
        {
            if (!pending || now - pressedAt > window)
                return false;

            pending = false;
            return true;
        }
    }

    // 카메라 벡터
    private Vector3 camForward;
    private Vector3 camRight;

    //Start에서 한 번 만들어 재사용한다. 인스펙터 값이 아니라 런타임 객체다
    public PlayerIdleState IdleState { get; private set; }
    public PlayerMoveState MoveState { get; private set; }
    public PlayerJumpState JumpState { get; private set; }
    public PlayerDashState DashState { get; private set; }
    private PlayerKnockbackState knockbackState;
    public PlayerAttackState AttackState { get; private set; }

    /// <summary>애니메이션 트리거를 네트워크로 알리는 창구. 루트에 붙어 있다.</summary>
    public LanPlayerVisual Visual { get; private set; }

    #endregion

    #region 플레이어 FSM

    // 메모리 낭비를 막기 위해 상태들을 미리 생성
    [Header("플레이어 FSM")]
    // 현재 상태
    private PlayerBaseState currentState;

    #endregion

    public float MoveSpeed { get { return moveSpeed; } set { moveSpeed = value; } }
    public float JumpForce { get { return jumpForce; } set { jumpForce = value; } }
    public float OriginalJumpForce { get { return originalJumpForce; } }
    public float DashSpeed { get { return dashSpeed; } }
    public float DashDuration { get { return dashDuration; } }
    //DashCooldown 게터를 지웠다. 쿨타임 HUD 가 남은 시간과 최대값을 묶은 DashCooldownInfo 를
    //읽게 바뀐 뒤로 부르는 곳이 없다

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
    public void MarkAsLocal() => Local = this;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInputLocked() => InputLocked = false;
    public Animator Anim { get { return animator; } }
    public Transform BatPivot { get { return batPivot; } }
    public bool HideBatWhenIdle { get { return hideBatWhenIdle; } }

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

            //잠금 직전에 누른 키가 풀리자마자 튀어나오지 않게 비운다
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

    //점프만 땅을 요구한다. 대쉬·공격은 공중에서도 된다
    public bool CanJump()
    {
        return !InputLocked
            && IsGrounded
            && currentState != JumpState
            && currentState != knockbackState;
    }

    /// <summary>
    /// 기억해 둔 입력으로 행동을 시작한다. 시작했으면 true — 부른 쪽은 그 프레임 처리를 멈춘다.
    ///
    /// ★ 행동으로 가는 전환은 전부 여기 하나에 있다
    ///   예전엔 Idle과 Move가 같은 입력 검사(공격 → 대쉬 → 점프)를 한 벌씩 들고 있어서
    ///   우선순위를 바꾸려면 두 곳을 같이 고쳐야 했다. Jump에서도 불러 공중 대쉬·공격을 연다.
    ///   입력은 조건이 맞을 때만 꺼내므로, 쿨타임 중에 누른 키는 기억 시간 동안 남아 있다.
    /// </summary>
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

    /// <summary>
    /// 행동(점프·대쉬·공격·넉백)이 끝났을 때 다음 상태를 정한다.
    /// 기억된 입력이 있으면 다음 행동으로, 없으면 Move 또는 Idle로 간다.
    /// TryStartAction과 짝이다 — 행동을 시작하는 곳과 끝내는 곳.
    ///
    /// ★ 기억해 둔 다음 행동이 있으면 이동 상태를 거치지 않고 바로 잇는다
    ///   Move를 거치면 그 Enter·Exit(걷기 효과음·애니메이션)가 같은 프레임에 켜졌다 꺼진다.
    ///   지금 상태와 같은 행동(대쉬 끝에 대쉬)이나 넉백 끝의 행동은 Can* 이 막으므로
    ///   이동 상태로 먼저 가고, 다음 프레임 그 상태의 TryStartAction이 이어받는다.
    ///
    ///   예전 Jump는 여기를 쓰지 않고 무조건 Idle로 갔다 — 이동키를 누른 채 착지해도
    ///   Idle을 한 프레임 거친 뒤에야 Move가 됐다.
    /// </summary>
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