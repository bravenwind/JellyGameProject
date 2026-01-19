using UnityEngine;

public class JellyMeshver2 : MonoBehaviour
{
    public float bounceSpeed = 20f; // 탄성 속도 (탱글거리는 속도)
    public float fallForce = 40f;   // 충격 흡수량
    public float stiffness = 5f;    // 딱딱한 정도

    public SkinnedMeshRenderer skinnedMeshRenderer;
    public Transform impactOrigin;
    private Mesh mesh;
    private Vector3[] originalVertices;
    private Vector3[] currentVertices;
    private Vector3[] vertexVelocities;

    private void Start()
    {
        mesh = skinnedMeshRenderer.sharedMesh; // 인스턴스 메쉬 생성

        // 버텍스 정보 저장
        originalVertices = mesh.vertices;
        currentVertices = new Vector3[originalVertices.Length];
        vertexVelocities = new Vector3[originalVertices.Length];

        for (int i = 0; i < originalVertices.Length; i++)
        {
            currentVertices[i] = originalVertices[i];
        }
    }

    private void FixedUpdate()
    {
        // 젤리 물리 연산
        for (int i = 0; i < currentVertices.Length; i++)
        {
            // 1. 목표 위치 계산 (원래 모양으로 돌아가려는 힘)
            Vector3 target = originalVertices[i];

            // 2. 현재 버텍스에 가해지는 힘 계산 (스프링 물리)
            Vector3 displacement = currentVertices[i] - target;
            Vector3 force = -stiffness * displacement; // Hooke's Law (F = -kx)

            // 3. 속도 및 위치 업데이트 (감쇠 적용)
            vertexVelocities[i] += force * Time.fixedDeltaTime;
            vertexVelocities[i] *= 1f - (fallForce * 0.01f); // 감쇠
            currentVertices[i] += vertexVelocities[i] * Time.fixedDeltaTime;
        }

        // 메쉬 업데이트
        mesh.vertices = currentVertices;
        mesh.RecalculateNormals(); // 조명 업데이트
        mesh.RecalculateBounds();  // 충돌 범위 등 업데이트
    }

    // 외부에서 충격을 주는 함수
    public void ApplyForce(Vector3 position, float force)
    {
        for (int i = 0; i < currentVertices.Length; i++)
        {
            // 월드 좌표로 변환하여 거리 계산
            Vector3 worldPt = transform.TransformPoint(currentVertices[i]);
            float distance = Vector3.Distance(worldPt, position);

            // 거리에 반비례하여 힘 적용
            if (distance < 1.0f) // 반경 1.0f 내의 버텍스만 흔들림
            {
                // 로컬 공간에서의 힘 방향 계산 (간단하게 랜덤이나 반대 방향)
                Vector3 pushDir = (worldPt - position).normalized;
                vertexVelocities[i] += transform.InverseTransformDirection(pushDir) * force / (distance + 0.1f);
            }
        }
    }

    // 테스트용: 마우스 클릭으로 찌르기
    private void Update()
    {
        ApplyForce(impactOrigin.position, 100f);
    }
}