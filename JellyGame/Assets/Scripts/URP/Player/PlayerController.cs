using UnityEngine;

public class PlayerController : BaseFSM
{
    [Header("Player Settings")]
    public float moveSpeed = 6.0f;
    public float autoRunSpeed = 8.0f; // 정찰(자동달리기) 속도
    public float rotateSpeed = 10.0f;

    [Header("Physics")]
    public float jumpForce = 7.5f;
    public float groundCheckDistance = 0.5f;

    public Animator jellyAnimator;

    // 입력 벡터를 캐싱하기 위한 변수
    private Vector3 inputDir;

    private Rigidbody rb;
    public bool isGrounded;

    private Vector3 camForward;
    private Vector3 camRight;

    private Collider playerCollider; // [추가] 콜라이더 참조 변수
    private Quaternion targetRotation;


    protected override void Start()
    {
        base.Start();
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate; // ⭐ 필수
        rb.freezeRotation = true; // 물리 회전 방지 (우리가 직접 제어)
        // [추가] 콜라이더 가져오기

        playerCollider = GetComponent<Collider>();

        UpdateCameraVectors();
    }

    protected override void Update()
    {
        base.Update();
        UpdateCameraVectors();
    }


    private Vector3 currentMoveDir = Vector3.zero;
    private bool isMovingState = false; // 현재 이동 상태인지 체크

    protected override void FixedUpdate()
    {
        CheckGround();

        if (!isMovingState) return;

        ApplyPhysicsMovement();
        ApplyPhysicsRotation();
    }

    private void CalculateMoveDirection()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        currentMoveDir = (camForward * v + camRight * h).normalized;

        // 회전 목표만 계산 (적용 ❌)
        if (currentMoveDir != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(currentMoveDir);
        }
    }


    // -----------------------------------------------------------------------
    // 1. IDLE 상태: 입력 대기
    // -----------------------------------------------------------------------
    protected override void Enter_Idle()
    {
        // 예: 멈춤 애니메이션, 물리 속도 초기화
        Debug.Log("[Player] 대기 상태 진입. 입력을 기다립니다.");
        inputDir = Vector3.zero;
    }

    protected override void Update_Idle()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ChangeState(UnitState.Jump);
            return;
        }

        // 1. 이동 입력 감지 시 -> Move 상태로 전환
        if (IsMoveInputActive())
        {
            ChangeState(UnitState.Move);
            return;
        }

        // 2. 'R'키 입력 시 -> Patrol(자동 달리기) 상태로 전환
        //if (Input.GetKeyDown(KeyCode.R))
        //{
        //    ChangeState(UnitState.Patrol);
        //}
    }

    // -----------------------------------------------------------------------
    // 2. MOVE 상태: WASD 직접 조작
    // -----------------------------------------------------------------------
    protected override void Enter_Move()
    {
        base.Enter_Move();
        isMovingState = true;
        jellyAnimator.SetBool("IsMoving", isMovingState);
    }

    private void CheckGround()
    {
        // 1. 레이 시작점 계산:
        // 콜라이더의 가장 밑바닥(min.y)에서 아주 살짝(0.05f) 위로 올린 지점
        Vector3 rayOrigin = transform.position;
        if (playerCollider != null)
        {
            rayOrigin.y = playerCollider.bounds.min.y + 0.05f;
        }

        // 2. 레이 쏘기
        RaycastHit hit;
        // 시작점에서 아래로 쏘는데, 거리는 아주 짧아도 됨 (이미 발바닥 근처니까)
        // groundCheckDistance를 0.1f~0.2f 정도로 짧게 줘도 충분합니다.
        isGrounded = Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out hit,
            groundCheckDistance
        );

        // [디버그용] 눈으로 확인하기 위해 빨간 선을 그립니다 (Scene 뷰에서 보임)
        Debug.DrawRay(rayOrigin, Vector3.down * groundCheckDistance, isGrounded ? Color.green : Color.red);
    }

    private void ApplyPhysicsRotation()
    {
        if (currentMoveDir == Vector3.zero) return;

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                rotateSpeed * Time.fixedDeltaTime
            )
        );
    }

    private void ApplyPhysicsMovement()
    {
        Vector3 velocity = rb.linearVelocity;
        velocity.x = currentMoveDir.x * moveSpeed;
        velocity.z = currentMoveDir.z * moveSpeed;
        rb.linearVelocity = velocity;
    }

    protected override void Update_Move()
    {
        // 1. 상태 체크 및 전환 로직 (그대로 유지)
        if (Input.GetKeyDown(KeyCode.Space)) { ChangeState(UnitState.Jump); return; }
        if (!IsMoveInputActive()) { ChangeState(UnitState.Idle); return; }
        //if (Input.GetKeyDown(KeyCode.R)) { ChangeState(UnitState.Patrol); }

        // 2. [수정] 여기서는 입력값만 계산하고, 실제 이동(linearVelocity 설정)은 하지 않음
        CalculateMoveDirection(); // ✅ 입력 & 회전 목표 계산만

        // 이동 상태임을 표시 (FixedUpdate에서 쓰기 위해)
        isMovingState = true;
    }

    protected override void Exit_Move()
    {
        base.Exit_Move();
        isMovingState = false;
        jellyAnimator.SetBool("IsMoving", isMovingState);
        // 멈출 때 미끄러짐 방지를 위해 속도 0으로 (선택사항)
        rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
    }

    // -----------------------------------------------------------------------
    // 3. PATROL 상태: 자동 달리기 (Auto-Run)
    // -----------------------------------------------------------------------
    //protected override void Enter_Patrol()
    //{
    //    Debug.Log("[Player] 자동 달리기 모드 활성화 (해제하려면 S키)");
    //    // 카메라 줌 아웃 효과 등 연출 추가 가능
    //}

    //protected override void Update_Patrol()
    //{
    //    if (Input.GetKeyDown(KeyCode.Space))
    //    {
    //        ChangeState(UnitState.Jump);
    //        return;
    //    }

    //    float v = Input.GetAxisRaw("Vertical");
    //    if (v < -0.1f)
    //    {
    //        ChangeState(UnitState.Idle);
    //        return;
    //    }

    //    // ✅ 자동 전진
    //    Vector3 velocity = rb.linearVelocity;
    //    velocity.x = transform.forward.x * autoRunSpeed;
    //    velocity.z = transform.forward.z * autoRunSpeed;
    //    rb.linearVelocity = velocity;

    //    // 회전
    //    float h = Input.GetAxisRaw("Horizontal");
    //    if (Mathf.Abs(h) > 0.01f)
    //    {
    //        transform.Rotate(Vector3.up * h * rotateSpeed * Time.deltaTime);
    //    }
    //}


    // -----------------------------------------------------------------------
    // Helper Methods (행동 정의)
    // -----------------------------------------------------------------------

    protected override void Enter_Jump()
    {
        //if (!isGrounded)
        //{
        //    ChangeState(UnitState.Idle);
        //    return;
        //}

        jellyAnimator.SetTrigger("Jump");

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }


    protected override void Update_Jump()
    {
        ProcessMovement(moveSpeed);

        CheckGround();

        if (isGrounded && rb.linearVelocity.y <= 0f)
        {
            ChangeState(UnitState.Idle);
        }
    }

    // 입력이 유효한지 체크 (최적화: sqrMagnitude 사용)
    private bool IsMoveInputActive()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // 벡터 생성 비용을 줄이기 위해 단순 절대값 비교가 더 빠를 수 있음
        // 여기서는 가독성을 위해 sqrMagnitude 패턴 사용
        Vector3 checkVec = camForward * v + camRight * h;
        return checkVec.sqrMagnitude > 0.001f;
    }

    // 실제 이동 처리를 담당하는 함수
    private void ProcessMovement(float speed)
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 moveDir = (camForward * v + camRight * h).normalized;

        Vector3 velocity = rb.linearVelocity;
        velocity.x = moveDir.x * speed;
        velocity.z = moveDir.z * speed;
        rb.linearVelocity = velocity;

        // 회전
        if (moveDir != Vector3.zero)
        {
            Quaternion lookRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                lookRot,
                rotateSpeed * Time.deltaTime
            );
        }
    }

    private void UpdateCameraVectors()
    {
        Transform cam = Camera.main.transform;

        camForward = cam.forward;
        camRight = cam.right;

        camForward.y = 0f;
        camRight.y = 0f;

        camForward.Normalize();
        camRight.Normalize();
    }

}