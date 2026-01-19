using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SkinnedMeshRenderer))]
public class SoftBody3D : MonoBehaviour
{
    [Header("Jelly Settings (실시간 수정 가능)")]
    [Tooltip("최대 이동 허용 거리 (크면 물주머니, 작으면 단단함)")]
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
    [Tooltip("이동 시 관성 영향 (크면 이동할 때 젤리가 뒤로 확 쏠림)")]
    [Range(0f, 5f)]
    public float worldVelocityScale = 1.5f;

    [Tooltip("가속 시 관성 영향")]
    [Range(0f, 5f)]
    public float worldAccelerationScale = 1.5f;

    private Cloth _cloth;
    private SkinnedMeshRenderer _skinnedMeshRenderer;
    private float _lastSoftness;

    private void Awake()
    {
        InitCloth();
        _lastSoftness = softness;
    }

    private void Update()
    {
        if (_cloth == null) return;

        // 파라미터 실시간 적용
        _cloth.damping = damping;
        _cloth.stretchingStiffness = stretchingStiffness;
        _cloth.bendingStiffness = bendingStiffness;
        _cloth.worldVelocityScale = worldVelocityScale;
        _cloth.worldAccelerationScale = worldAccelerationScale;
        _cloth.useGravity = false;

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
        {
            _cloth = gameObject.AddComponent<Cloth>();
        }

        // 초기 셋팅 적용
        ApplyClothSettings();
        UpdateSoftness();
    }

    // 설정값 일괄 적용 함수 (코드 중복 방지)
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
        if (_skinnedMeshRenderer == null || _cloth == null) return;

        int vertexCount = _skinnedMeshRenderer.sharedMesh.vertices.Length;
        ClothSkinningCoefficient[] coefficients = new ClothSkinningCoefficient[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            coefficients[i].maxDistance = softness;
            coefficients[i].collisionSphereDistance = 0.0f;
        }

        _cloth.coefficients = coefficients;
    }

    public void DisableCloth()
    {
        if (_cloth != null)
            _cloth.enabled = false;
    }

    // void -> IEnumerator로 변경
    public IEnumerator EnableAndRebuildCloth()
    {
        if (_skinnedMeshRenderer == null) yield break;

        // 1. 기존 Cloth 컴포넌트 삭제
        if (_cloth != null)
        {
            DestroyImmediate(_cloth);
        }

        // 🔥 [핵심] 1프레임 대기! 
        // Cloth가 사라지고 메쉬가 '일반 상태(Static)'로 돌아올 시간을 줍니다.
        // 이 찰나의 순간에는 젤리가 딱딱하게 보이지만, 깜빡임(사라짐)은 발생하지 않습니다.
        yield return null;

        // 2. 안전장치: 바운드 강제 확장 (혹시 모를 Culling 방지)
        _skinnedMeshRenderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        _skinnedMeshRenderer.updateWhenOffscreen = true;

        // 3. Cloth 컴포넌트 새로 추가
        _cloth = gameObject.AddComponent<Cloth>();

        // 4. 설정값 복구 및 Coefficient 재계산
        ApplyClothSettings();
        UpdateSoftness();

        _cloth.enabled = true;
    }

    public IEnumerator RebuildCloth_NoFlicker()
    {
        if (_cloth == null) yield break;

        // 1. Cloth 계산만 잠시 멈춤
        _cloth.enabled = false;

        // 2. 스케일 반영을 위한 1프레임 대기
        yield return new WaitForEndOfFrame();

        // 3. Coefficient 재계산
        UpdateSoftness();
        ApplyClothSettings();

        // 4. 다시 활성화
        _cloth.enabled = true;
    }

    public IEnumerator RebuildCloth_AfterScale()
    {
        if (_cloth == null || _skinnedMeshRenderer == null)
            yield break;

        // 1. Cloth 정지
        _cloth.enabled = false;

        // 2. 스케일 적용 대기
        yield return new WaitForEndOfFrame();

        // 🔥 3. 핵심: Transform Motion 강제 리셋
        _cloth.ClearTransformMotion();

        // 4. 계수 재적용
        ApplyClothSettings();
        UpdateSoftness();

        // 5. 다시 활성화
        _cloth.enabled = true;

        // 6. 관성 계산 안정화
        yield return null;
    }

    public void OnParentScaleFinished()
    {
        _cloth.ClearTransformMotion();
    }

}