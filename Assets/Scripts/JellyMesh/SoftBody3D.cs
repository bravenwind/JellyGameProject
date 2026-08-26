using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SkinnedMeshRenderer))]
public class SoftBody3D : MonoBehaviour
{
    [Header("Jelly Settings")]
    [Tooltip("체크 시 에디터에서 칠한 값(0인 부분)은 유지하고, 나머지만 Softness로 제어합니다.")]
    [SerializeField] private bool useHybridSoftness = true;

    [Tooltip("최대 이동 허용 거리 (칠해진 부분을 제외한 나머지 젤리 부분의 출렁임 정도)")]
    [Range(0f, 100f)]
    [SerializeField] private float softness = 0.5f;

    [Tooltip("공기 저항 (0에 가까울수록 계속 출렁거림)")]
    [Range(0f, 1f)]
    [SerializeField] private float damping = 0.01f;

    [Tooltip("형태 유지력 (1이면 잘 안 늘어남)")]
    [Range(0f, 1f)]
    [SerializeField] private float stretchingStiffness = 1.0f;

    [Tooltip("굽힘 강도 (0에 가까울수록 표면이 잘 구겨짐)")]
    [Range(0f, 1f)]
    [SerializeField] private float bendingStiffness = 0.1f;

    [Header("Motion Settings")]
    [Range(0f, 5f)] [SerializeField] private float worldVelocityScale = 0.3f;
    [Range(0f, 5f)] [SerializeField] private float worldAccelerationScale = 0.3f;

    private Cloth cloth;
    private SkinnedMeshRenderer skinnedMeshRenderer;
    private float lastSoftness;
    private bool isRebuilding = false;

    // 🔥 에디터에서 칠한 초기값을 저장할 배열
    private ClothSkinningCoefficient[] initialCoefficients;

    private void Awake()
    {
        InitCloth();
        lastSoftness = softness;
    }

    private void Update()
    {
        if (cloth == null)
            return;

        cloth.damping = damping;
        cloth.stretchingStiffness = stretchingStiffness;
        cloth.bendingStiffness = bendingStiffness;
        cloth.worldVelocityScale = worldVelocityScale;
        cloth.worldAccelerationScale = worldAccelerationScale;
        cloth.useGravity = true;

        if (!Mathf.Approximately(lastSoftness, softness))
        {
            UpdateSoftness();
            lastSoftness = softness;
        }
    }

    void InitCloth()
    {
        skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
        if (skinnedMeshRenderer == null)
            return;

        cloth = GetComponent<Cloth>();
        if (cloth == null)
            cloth = gameObject.AddComponent<Cloth>();

        ApplyClothSettings();

        // 1. 게임 시작 시 에디터에서 칠한 추가 제약 적용
        initialCoefficients = cloth.coefficients;

        UpdateSoftness();
    }

    void ApplyClothSettings()
    {
        if (cloth == null)
            return;
        cloth.damping = damping;
        cloth.stretchingStiffness = stretchingStiffness;
        cloth.bendingStiffness = bendingStiffness;
        cloth.worldVelocityScale = worldVelocityScale;
        cloth.worldAccelerationScale = worldAccelerationScale;
        cloth.useGravity = false;
    }

    void UpdateSoftness()
    {
        if (skinnedMeshRenderer == null || cloth == null || initialCoefficients == null)
            return;

        int vertexCount = initialCoefficients.Length;
        ClothSkinningCoefficient[] currentCoefficients = new ClothSkinningCoefficient[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            if (useHybridSoftness)
            {
                if (initialCoefficients[i].maxDistance < softness)
                    currentCoefficients[i].maxDistance = initialCoefficients[i].maxDistance;
                else
                    currentCoefficients[i].maxDistance = softness;
            }
            else
                currentCoefficients[i].maxDistance = softness;

            currentCoefficients[i].collisionSphereDistance = 0.0f;
        }

        cloth.coefficients = currentCoefficients;
    }

    public void DisableCloth()
    {
        if (cloth != null)
            cloth.enabled = false;
    }

    public void RemoveCloth()
    {
        Mesh cachedMesh = skinnedMeshRenderer != null ? skinnedMeshRenderer.sharedMesh : null;

        if (cloth != null)
            Destroy(cloth);
        cloth = null;

        if (skinnedMeshRenderer != null)
        {
            skinnedMeshRenderer.updateWhenOffscreen = true;
            if (cachedMesh != null)
                skinnedMeshRenderer.sharedMesh = cachedMesh;
        }
        enabled = false;
    }

    public void RequestRebuildCloth()
    {
        if (!gameObject.activeInHierarchy)
            return;
        if (isRebuilding)
            return;
        StartCoroutine(EnableAndRebuildCloth());
    }

    public IEnumerator EnableAndRebuildCloth()
    {
        if (skinnedMeshRenderer == null)
            yield break;
        if (isRebuilding)
            yield break;

        isRebuilding = true;

        // 기존 Cloth 제거 (렌더러는 끄지 않아 깜빡임 방지)
        if (cloth != null)
            Destroy(cloth);

        // Destroy 반영 대기 (2프레임)
        yield return null;
        yield return null;

        // SetActive(false)로 인해 오브젝트가 비활성화되었으면 중단
        if (!gameObject.activeInHierarchy)
        {
            isRebuilding = false;
            yield break;
        }

        // 현재 포즈 위에서 Cloth 새로 생성
        cloth = gameObject.AddComponent<Cloth>();

        initialCoefficients = cloth.coefficients;
        ApplyClothSettings();
        UpdateSoftness();

        cloth.ClearTransformMotion();
        cloth.enabled = true;

        isRebuilding = false;
    }
}