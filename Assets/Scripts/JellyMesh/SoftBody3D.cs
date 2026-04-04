using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SkinnedMeshRenderer))]
public class SoftBody3D : MonoBehaviour
{
    [Header("Jelly Settings")]
    [Tooltip("체크 시 에디터에서 칠한 값(0인 부분)은 유지하고, 나머지만 Softness로 제어합니다.")]
    public bool useHybridSoftness = true;

    [Tooltip("최대 이동 허용 거리 (칠해진 부분을 제외한 나머지 젤리 부분의 출렁임 정도)")]
    [Range(0f, 100f)]
    public float softness = 0.5f;

    [Tooltip("공기 저항 (0에 가까울수록 계속 출렁거림)")]
    [Range(0f, 1f)]
    public float damping = 0.01f;

    [Tooltip("형태 유지력 (1이면 잘 안 늘어남)")]
    [Range(0f, 1f)]
    public float stretchingStiffness = 1.0f;

    [Tooltip("굽힘 강도 (0에 가까울수록 표면이 잘 구겨짐)")]
    [Range(0f, 1f)]
    public float bendingStiffness = 0.1f;

    [Header("Motion Settings")]
    [Range(0f, 5f)] public float worldVelocityScale = 0.3f;
    [Range(0f, 5f)] public float worldAccelerationScale = 0.3f;

    private Cloth _cloth;
    private SkinnedMeshRenderer _skinnedMeshRenderer;
    private float _lastSoftness;

    // 🔥 에디터에서 칠한 초기값을 저장할 배열
    private ClothSkinningCoefficient[] _initialCoefficients;

    private void Awake()
    {
        InitCloth();
        _lastSoftness = softness;
    }

    private void Update()
    {
        if (_cloth == null) return;

        _cloth.damping = damping;
        _cloth.stretchingStiffness = stretchingStiffness;
        _cloth.bendingStiffness = bendingStiffness;
        _cloth.worldVelocityScale = worldVelocityScale;
        _cloth.worldAccelerationScale = worldAccelerationScale;
        _cloth.useGravity = true;

        if (!Mathf.Approximately(_lastSoftness, softness))
        {
            UpdateSoftness();
            _lastSoftness = softness;
        }
    }

    void InitCloth()
    {
        _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
        if (_skinnedMeshRenderer == null) return;

        _cloth = GetComponent<Cloth>();
        if (_cloth == null)
            _cloth = gameObject.AddComponent<Cloth>();

        ApplyClothSettings();

        // 1. 게임 시작 시 에디터에서 칠한 추가 제약 적용
        _initialCoefficients = _cloth.coefficients;

        UpdateSoftness();
    }

    void ApplyClothSettings()
    {
        if (_cloth == null) return;
        _cloth.damping = damping;
        _cloth.stretchingStiffness = stretchingStiffness;
        _cloth.bendingStiffness = bendingStiffness;
        _cloth.worldVelocityScale = worldVelocityScale;
        _cloth.worldAccelerationScale = worldAccelerationScale;
        _cloth.useGravity = false;
    }

    void UpdateSoftness()
    {
        if (_skinnedMeshRenderer == null || _cloth == null || _initialCoefficients == null) return;

        int vertexCount = _initialCoefficients.Length;
        ClothSkinningCoefficient[] currentCoefficients = new ClothSkinningCoefficient[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            if (useHybridSoftness)
            {
                if (_initialCoefficients[i].maxDistance < softness)
                {
                    currentCoefficients[i].maxDistance = _initialCoefficients[i].maxDistance;
                }
                else
                {
                    currentCoefficients[i].maxDistance = softness;
                }
            }
            else
            {
                currentCoefficients[i].maxDistance = softness;
            }

            currentCoefficients[i].collisionSphereDistance = 0.0f;
        }

        _cloth.coefficients = currentCoefficients;
    }

    public void DisableCloth()
    {
        if (_cloth != null)
            _cloth.enabled = false;
    }


    public IEnumerator EnableAndRebuildCloth()
    {
        if (_skinnedMeshRenderer == null) yield break;

        Animator animator = GetComponentInParent<Animator>();

        // ✅ [핵심 수정] 클로스 생성 전, 캐릭터를 강제로 "기본 자세"로 초기화합니다.
        if (animator != null)
        {
            animator.Rebind();   // 애니메이터를 초기 상태(T-포즈 또는 Idle)로 리셋
            animator.Update(0f); // 리셋된 자세를 1프레임도 기다리지 않고 메쉬에 즉시 적용
            animator.enabled = false; // 기본 자세 그대로 정지
        }

        // 1. 기존 Cloth 컴포넌트 삭제
        if (_cloth != null) DestroyImmediate(_cloth);

        // 2. 메쉬가 기본 자세로 완전히 펴질 때까지 2프레임 대기
        yield return null;
        yield return null;

        // 3. 반듯한 기본 자세 위에서 Cloth 새로 생성
        _cloth = gameObject.AddComponent<Cloth>();

        // 4. 물리 설정 및 소프트니스 적용
        ApplyClothSettings();
        UpdateSoftness();

        // 5. 물리 엔진 관성 무시
        _cloth.ClearTransformMotion();

        _cloth.enabled = true;

        // ✅ [추가 및 수정] 안정화가 끝난 후 애니메이션 상태 즉시 복구
        if (animator != null)
        {
            animator.enabled = true;

            // 부모 오브젝트의 플레이어 컨트롤러를 가져옵니다.
            PlayerController playerController = GetComponentInParent<PlayerController>();
            if (playerController != null && playerController.enabled)
            {
                // 1. 공중에 떠 있다면 점프 모션 즉시 재생
                if (!playerController.isGrounded)
                {
                    animator.SetTrigger("Jump");
                }
                else
                {
                    // 2. 바닥에 있고 이동 키를 누르고 있다면 걷기 모션 즉시 재생
                    float h = Input.GetAxis("Horizontal");
                    float v = Input.GetAxis("Vertical");
                    bool isMoving = (h * h + v * v) > 0.001f;

                    // 애니메이터 파라미터에 현재 이동 여부를 바로 덮어씁니다.
                    animator.SetBool("IsMoving", isMoving);
                }
            }
        }
    }
}