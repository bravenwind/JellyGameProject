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

        // 1. 게임 시작 시 에디터에서 칠한 제약 조건 백업
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

        // 🔥 수정된 부분: 메쉬의 정점 개수가 아닌, 실제 Cloth(initialCoefficients)의 개수를 기준으로 설정
        int vertexCount = _initialCoefficients.Length;
        ClothSkinningCoefficient[] currentCoefficients = new ClothSkinningCoefficient[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            if (useHybridSoftness)
            {
                // _initialCoefficients 배열의 범위를 벗어나지 않으므로 안전함
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

    // 🔥 타이머 및 스케일 변경에서 호출할 함수들 (추가됨)
    public void DisableCloth()
    {
        if (_cloth != null)
            _cloth.enabled = false;
    }

    // 🔥 스케일 변경 후 버그 없이 천을 재구성하는 핵심 함수
    public IEnumerator EnableAndRebuildCloth()
    {
        if (_skinnedMeshRenderer == null) yield break;

        // 1. 기존 Cloth 컴포넌트 삭제 (찌그러짐 방지)
        if (_cloth != null) DestroyImmediate(_cloth);

        // 2. 1프레임 대기 (메쉬가 스케일에 맞춰 안정화될 시간)
        yield return null;

        // 3. Cloth 새로 생성
        _cloth = gameObject.AddComponent<Cloth>();

        // 4. 물리 설정 및 하이브리드 소프트니스 재적용! (팔은 안 늘어나게)
        ApplyClothSettings();
        UpdateSoftness();

        _cloth.enabled = true;
    }
}