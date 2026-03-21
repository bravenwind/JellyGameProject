//using UnityEngine;

//public class PlayerBearController : BaseFSM
//{
//    [Header("Player Settings")]
//    public float moveSpeed = 6.0f;
//    public float autoRunSpeed = 8.0f;
//    public float rotateSpeed = 10.0f;

//    [Header("Physics")]
//    public float jumpForce = 7.5f;
//    public float groundCheckDistance = 0.5f;

//    [Header("Model Settings")]
//    // 🔥 모델이 눕지 않도록 보정하는 오프셋 값 (-90, 0, 0)
//    private Quaternion modelCorrection = Quaternion.Euler(-90, 0, 0);

//    public Animator jellyAnimator;

//    private Vector3 inputDir;
//    public Rigidbody rb;
//    public bool isGrounded;

//    private Vector3 camForward;
//    private Vector3 camRight;

//    private Collider playerCollider;
//    private Quaternion targetRotation;

//    protected override void Start()
//    {
//        base.Start();
//        // rb = GetComponent<Rigidbody>();
//        // 🔥 중요: 물리 회전은 코드로 제어하므로 Rigidbody에서 잠가야 합니다.
//        // (인스펙터에서 Freeze Rotation X, Y, Z를 모두 체크하는 것을 권장)
//        if (rb != null) rb.freezeRotation = true;

//        playerCollider = GetComponent<Collider>();
//        UpdateCameraVectors();

//        // 시작 시 현재 회전값 초기화 (오류 방지)
//        targetRotation = transform.rotation;
//    }

//    protected override void Update()
//    {
//        base.Update();
//        UpdateCameraVectors();
//    }

//    private Vector3 currentMoveDir = Vector3.zero;
//    private bool isMovingState = false;

//    protected override void FixedUpdate()
//    {
//        CheckGround();

//        if (!isMovingState) return;

//        ApplyPhysicsMovement();
//        ApplyPhysicsRotation();
//    }

//    private void CalculateMoveDirection()
//    {
//        float h = Input.GetAxis("Horizontal");
//        float v = Input.GetAxis("Vertical");

//        currentMoveDir = (camForward * v + camRight * h).normalized;

//        // 🔥 [수정됨] 회전 목표 계산 로직 변경
//        if (currentMoveDir != Vector3.zero)
//        {
//            // 1. 진행 방향을 바라보는 기본 회전을 구함 (이 상태면 모델이 누움)
//            Quaternion lookRot = Quaternion.LookRotation(currentMoveDir);

//            // 2. 구한 회전에 오프셋(-90도)을 곱해서 모델을 일으켜 세움
//            targetRotation = lookRot * modelCorrection;
//        }
//    }

//    // ... (Enter_Idle, Update_Idle, Enter_Move 등 기존 코드 동일) ...
//    // ... 생략된 부분은 원본과 동일하게 유지하세요 ...

//    protected override void Enter_Idle()
//    {
//        Debug.Log("[Player] 대기 상태 진입.");
//        inputDir = Vector3.zero;
//        // 멈출 때 미끄러짐 방지
//        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
//    }

//    protected override void Update_Idle()
//    {
//        if (Input.GetKeyDown(KeyCode.Space)) { ChangeState(UnitState.Jump); return; }
//        if (IsMoveInputActive()) { ChangeState(UnitState.Move); return; }
//    }

//    protected override void Enter_Move()
//    {
//        base.Enter_Move();
//        isMovingState = true;
//        if (jellyAnimator != null) jellyAnimator.SetBool("IsMoving", isMovingState);
//    }

//    protected override void Update_Move()
//    {
//        if (Input.GetKeyDown(KeyCode.Space)) { ChangeState(UnitState.Jump); return; }
//        if (!IsMoveInputActive()) { ChangeState(UnitState.Idle); return; }

//        CalculateMoveDirection(); // 입력 계산
//        isMovingState = true;
//    }

//    protected override void Exit_Move()
//    {
//        base.Exit_Move();
//        isMovingState = false;
//        if (jellyAnimator != null) jellyAnimator.SetBool("IsMoving", isMovingState);
//        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
//    }

//    private void CheckGround()
//    {
//        Vector3 rayOrigin = transform.position;
//        if (playerCollider != null)
//        {
//            // Bounds는 월드 좌표계 기준이므로 회전값과 상관없이 정확한 바닥을 줍니다.
//            rayOrigin.y = playerCollider.bounds.min.y + 0.05f;
//        }

//        RaycastHit hit;
//        isGrounded = Physics.Raycast(rayOrigin, Vector3.down, out hit, groundCheckDistance);
//        Debug.DrawRay(rayOrigin, Vector3.down * groundCheckDistance, isGrounded ? Color.green : Color.red);
//    }

//    private void ApplyPhysicsRotation()
//    {
//        if (currentMoveDir == Vector3.zero) return;

//        // Rigidbody를 통한 부드러운 회전
//        rb.MoveRotation(
//            Quaternion.Slerp(
//                rb.rotation,
//                targetRotation,
//                rotateSpeed * Time.fixedDeltaTime
//            )
//        );
//    }

//    private void ApplyPhysicsMovement()
//    {
//        Vector3 velocity = rb.linearVelocity;
//        // 이동은 월드 좌표계 기준이므로 로컬 회전(-90)과 상관없이 X, Z로 움직이면 됩니다.
//        velocity.x = currentMoveDir.x * moveSpeed;
//        velocity.z = currentMoveDir.z * moveSpeed;
//        rb.linearVelocity = velocity;
//    }

//    // -----------------------------------------------------------------------
//    // 점프 상태 (Update_Jump 내부의 회전 로직도 수정 필요)
//    // -----------------------------------------------------------------------

//    protected override void Enter_Jump()
//    {
//        if (jellyAnimator != null) jellyAnimator.SetTrigger("Jump");

//        // 점프 시 수평 속도 유지 or 초기화? (취향에 따라 선택)
//        // rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

//        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
//    }

//    protected override void Update_Jump()
//    {
//        // 공중 제어 (Air Control)
//        ProcessMovement(moveSpeed);
//        CheckGround();

//        if (isGrounded && rb.linearVelocity.y <= 0f)
//        {
//            ChangeState(UnitState.Idle);
//        }
//    }

//    // ProcessMovement 함수 내 회전 로직도 수정해야 함
//    private void ProcessMovement(float speed)
//    {
//        float h = Input.GetAxis("Horizontal");
//        float v = Input.GetAxis("Vertical");

//        Vector3 moveDir = (camForward * v + camRight * h).normalized;

//        Vector3 velocity = rb.linearVelocity;
//        velocity.x = moveDir.x * speed;
//        velocity.z = moveDir.z * speed;
//        rb.linearVelocity = velocity;

//        // 🔥 [수정됨] 공중에서의 회전 처리
//        if (moveDir != Vector3.zero)
//        {
//            // 1. 바라보는 방향
//            Quaternion lookRot = Quaternion.LookRotation(moveDir);
//            // 2. 오프셋 적용 (-90도)
//            Quaternion targetRot = lookRot * modelCorrection;

//            transform.rotation = Quaternion.Slerp(
//                transform.rotation,
//                targetRot,
//                rotateSpeed * Time.deltaTime
//            );
//        }
//    }

//    private bool IsMoveInputActive()
//    {
//        float h = Input.GetAxis("Horizontal");
//        float v = Input.GetAxis("Vertical");
//        return (camForward * v + camRight * h).sqrMagnitude > 0.001f;
//    }

//    private void UpdateCameraVectors()
//    {
//        if (Camera.main == null) return;
//        Transform cam = Camera.main.transform;

//        camForward = cam.forward;
//        camRight = cam.right;

//        camForward.y = 0f;
//        camRight.y = 0f;

//        camForward.Normalize();
//        camRight.Normalize();
//    }
//}