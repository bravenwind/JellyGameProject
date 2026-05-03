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
                    currentCoefficients[i].maxDistance = _initialCoefficients[i].maxDistance;
                else
                    currentCoefficients[i].maxDistance = softness;
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

        // Rebind() 없이 현재 포즈에서 멈춤
        if (animator != null) animator.enabled = false;

        // Cloth destroy/recreate 사이 1~2프레임 동안 SMR이 중간 상태로
        // 렌더링되어 깜빡이거나 투명해지는 현상을 방지
        _skinnedMeshRenderer.enabled = false;

        // 2. 2프레임 대기
        yield return null;
        yield return null;

        // 3. 현재 포즈 위에서 Cloth 새로 생성
        _cloth = gameObject.AddComponent<Cloth>();

        // 4. 새 Cloth의 기본 계수를 저장 후 소프트니스 적용
        _initialCoefficients = _cloth.coefficients;
        ApplyClothSettings();
        UpdateSoftness();

        // 5. 물리 엔진 관성 무시
        _cloth.ClearTransformMotion();
        _cloth.enabled = true;

        // 6. 렌더러 복구 후 애니메이터 재개
        _skinnedMeshRenderer.enabled = true;
        if (animator != null) animator.enabled = true;
    }
}