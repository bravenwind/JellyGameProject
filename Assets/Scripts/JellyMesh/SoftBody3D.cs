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

        // 1. 현재 상태 저장
        int stateHash = 0;
        float normalizedTime = 0f;
        if (animator != null && animator.isInitialized)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            stateHash = info.fullPathHash;
            normalizedTime = info.normalizedTime;
        }

        // 2. Cloth 재생성 (Rebind은 그대로 유지)
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
            animator.enabled = false;
        }

        if (_cloth != null) DestroyImmediate(_cloth);
        yield return null;
        yield return null;

        _cloth = gameObject.AddComponent<Cloth>();
        ApplyClothSettings();
        UpdateSoftness();
        _cloth.ClearTransformMotion();
        _cloth.enabled = true;

        // 3. 저장했던 상태로 즉시 복원 (Idle로 튀지 않음)
        if (animator != null)
        {
            animator.enabled = true;
            animator.Play(stateHash, 0, normalizedTime % 1f);
            animator.Update(0f); // 복원된 포즈를 즉시 메쉬에 반영
        }
    }
}