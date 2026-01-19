using UnityEngine;

public class CircleSpawner : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("생성할 프리팹 (적, 아이템 등)")]
    public GameObject[] jellies;

    [Tooltip("중심점이 될 대상 (플레이어 등). 비워두면 이 오브젝트 기준.")]
    public Transform centerTarget;

    [Tooltip("원 반지름")]
    public float radius = 5.0f;

    [Tooltip("생성된 물체가 중심을 바라보게 할지 여부")]
    public bool lookAtCenter = true;

    private void Start()
    {
        // 타겟이 설정 안 되어 있으면 자기 자신을 기준으로 함
        if (centerTarget == null) centerTarget = this.transform;
    }

    private void Update()
    {
        // 테스트용: J 키를 누르면 생성
        if (Input.GetKeyDown(KeyCode.J))
        {
            SpawnOnCircleEdge();
        }
    }

    // 외부에서 호출 가능한 생성 함수
    public void SpawnOnCircleEdge()
    {
        if (jellies == null) return;

        // 1. 0 ~ 360도 사이의 랜덤 각도 생성 (라디안 변환 필요)
        float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;

        // 2. 삼각함수로 X, Z 좌표 구하기
        // x = cos(각도) * 반지름
        // z = sin(각도) * 반지름
        float xOffset = Mathf.Cos(randomAngle) * radius;
        float zOffset = Mathf.Sin(randomAngle) * radius;

        // 3. 최종 생성 위치 계산 (타겟 위치 + 오프셋)
        // 높이(y)는 타겟과 동일하게 맞춤
        Vector3 spawnPos = centerTarget.position + new Vector3(xOffset, 0, zOffset);

        int randomIndex = Random.Range(0, jellies.Length);

        // 3. 해당 인덱스의 젤리 소환 (현재 위치와 회전값 기준)
        GameObject obj = Instantiate(jellies[randomIndex], spawnPos, Quaternion.identity);

        // 옵션: 생성된 물체가 중심(플레이어)을 바라보게 회전
        if (lookAtCenter)
        {
            // obj.transform.LookAt(centerTarget); // 간단한 방법
            
            // Y축 회전만 반영하고 싶다면 아래 방식 사용 (깔끔함)
            Vector3 direction = (centerTarget.position - spawnPos).normalized;
            direction.y = 0; // 높이 차이 무시
            if (direction != Vector3.zero)
            {
                obj.transform.rotation = Quaternion.LookRotation(direction);
            }
        }
    }

    // 에디터에서 원의 범위를 눈으로 확인하기 위한 기즈모
    private void OnDrawGizmos()
    {
        Transform target = centerTarget != null ? centerTarget : transform;

        // 원 그리기 (녹색)
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(target.position, radius);
        
        // 생성될 가능성이 있는 테두리 표시 (노란색 선)
        // 씬 뷰 바닥에 원을 그려서 보여줌 (UnityEditor 핸들 사용이 더 좋으나 간단히 구현)
        Gizmos.color = Color.yellow;
        Vector3 prevPos = target.position + new Vector3(radius, 0, 0);
        for (int i = 1; i <= 36; i++) // 10도씩 끊어서 그림
        {
            float angle = i * 10 * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            Vector3 newPos = target.position + new Vector3(x, 0, z);
            
            Gizmos.DrawLine(prevPos, newPos);
            prevPos = newPos;
        }
    }
}