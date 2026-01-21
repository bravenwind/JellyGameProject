using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomJellySpawner : MonoBehaviour
{
    [Header("필수 연결")]
    public AutoGridMapGenerator mapGenerator; // 맵 생성기
    public GameObject[] objectPrefab;           // 소환할 오브젝트
    public Camera gameCamera;                 // 게임 메인 카메라 (없으면 자동 할당)

    [Header("생성 설정")]
    public int maxObjectCount = 200;          // 최대 오브젝트 개수
    public float spawnInterval = 2.0f;        // 생성 간격 (초)
    public float yOffset = 1.0f;              // 높이 조절

    [Header("디버그")]
    public bool isSpawning = true;            // 스폰 활성화 여부

    [Header("배치 설정")]
    public int batchSpawnCount = 100; // 한 번에 배치할 개수

    // 관리 리스트
    private List<GameObject> spawnedObjects = new List<GameObject>();

    private void Start()
    {
        // 카메라가 연결 안 되어 있으면 메인 카메라 찾기
        if (gameCamera == null) gameCamera = Camera.main;

        // 자동 생성 코루틴 시작
        StartCoroutine(AutoSpawnRoutine());
    }

    // 일정 간격으로 생성하는 코루틴
    IEnumerator AutoSpawnRoutine()
    {
        while (true)
        {
            // 1. 간격 대기
            yield return new WaitForSeconds(spawnInterval);

            if (!isSpawning) continue;

            // 2. 리스트 청소 (파괴된 오브젝트 제거)
            // 리스트 요소 중 null인 것(이미 게임에서 삭제된 것)을 모두 지움
            spawnedObjects.RemoveAll(item => item == null);

            // 3. 최대 개수 체크
            if (spawnedObjects.Count >= maxObjectCount)
            {
                // 꽉 찼으면 생성 스킵
                continue;
            }

            // 4. 카메라 밖 위치 찾기 및 생성 시도
            TrySpawnOffScreen();
        }
    }

    [ContextMenu("랜덤 100개 즉시 배치")]
    public void SpawnRandomBatchDefault()
    {
        SpawnRandomBatch(batchSpawnCount);
    }

    /// <summary>
    /// 주어진 개수만큼 맵 영역 내에 랜덤하게 오브젝트를 배치합니다. (카메라 가림 여부 상관없음)
    /// </summary>
    public void SpawnRandomBatch(int count)
    {
        if (mapGenerator == null || objectPrefab == null || objectPrefab.Length == 0)
        {
            Debug.LogWarning("RandomJellySpawner: 필수 연결 요소가 누락되었습니다.");
            return;
        }

        // 1. 맵 크기 및 범위 계산 (기존 TrySpawnOffScreen 로직 활용)
        Renderer tileRenderer = mapGenerator.tilePrefab.GetComponent<Renderer>();
        if (tileRenderer == null) tileRenderer = mapGenerator.tilePrefab.GetComponentInChildren<Renderer>();

        Vector3 tileSize = tileRenderer.bounds.size;
        float stepX = tileSize.x + mapGenerator.gap;
        float stepZ = tileSize.z + mapGenerator.gap;
        float mapSizeX = (mapGenerator.width - 1) * stepX;
        float mapSizeZ = (mapGenerator.height - 1) * stepZ;

        // 2. 지정된 개수만큼 반복 생성
        for (int i = 0; i < count; i++)
        {
            float rX = Random.Range(0f, mapSizeX);
            float rZ = Random.Range(0f, mapSizeZ);

            if (mapGenerator.centerGrid)
            {
                rX -= mapSizeX / 2f;
                rZ -= mapSizeZ / 2f;
            }

            Vector3 spawnPos = mapGenerator.transform.position + new Vector3(rX, yOffset, rZ);

            // 3. 프리팹 랜덤 선택 및 생성
            int jellyIndex = Random.Range(0, objectPrefab.Length);
            GameObject obj = Instantiate(objectPrefab[jellyIndex], spawnPos, Quaternion.identity);

            // 4. 관리 설정
            obj.transform.SetParent(this.transform);
            obj.name = $"{objectPrefab[jellyIndex].name}_Batch_{System.DateTime.Now.Ticks}_{i}";

            spawnedObjects.Add(obj);
        }

        Debug.Log($"RandomJellySpawner: {count}개의 오브젝트를 배치했습니다.");
    }

    void TrySpawnOffScreen()
    {
        if (mapGenerator == null || objectPrefab == null) return;

        // 맵 크기 계산 (매번 계산하거나, Start에서 캐싱해도 됨)
        // 여기서는 안전하게 매번 계산
        Renderer tileRenderer = mapGenerator.tilePrefab.GetComponent<Renderer>();
        if (tileRenderer == null) tileRenderer = mapGenerator.tilePrefab.GetComponentInChildren<Renderer>();

        Vector3 tileSize = tileRenderer.bounds.size;
        float stepX = tileSize.x + mapGenerator.gap;
        float stepZ = tileSize.z + mapGenerator.gap;
        float mapSizeX = (mapGenerator.width - 1) * stepX;
        float mapSizeZ = (mapGenerator.height - 1) * stepZ;

        // 유효한 위치를 찾을 때까지 몇 번 시도할지 (무한 루프 방지)
        int maxAttempts = 10;
        Vector3 finalPos = Vector3.zero;
        bool foundValidPos = false;

        for (int i = 0; i < maxAttempts; i++)
        {
            // 랜덤 위치 계산
            float rX = Random.Range(0f, mapSizeX);
            float rZ = Random.Range(0f, mapSizeZ);

            if (mapGenerator.centerGrid)
            {
                rX -= mapSizeX / 2f;
                rZ -= mapSizeZ / 2f;
            }

            Vector3 candidatePos = mapGenerator.transform.position + new Vector3(rX, yOffset, rZ);

            // ** 핵심: 카메라 화면 안에 있는지 검사 **
            if (!IsVisibleFromCamera(candidatePos))
            {
                finalPos = candidatePos;
                foundValidPos = true;
                break; // 화면 밖 위치를 찾았으니 루프 탈출
            }
        }

        // 유효한 위치를 찾았다면 오브젝트 생성
        if (foundValidPos)
        {
            int jellyIndex = Random.Range(0, objectPrefab.Length);
            GameObject obj = Instantiate(objectPrefab[jellyIndex], finalPos, Quaternion.identity);

            obj.transform.SetParent(this.transform);
            obj.name = $"{objectPrefab[jellyIndex].name}_Auto_{Time.time}";

            spawnedObjects.Add(obj);
        }
    }

    // 위치가 카메라 화면 안에 들어오는지 확인하는 함수
    bool IsVisibleFromCamera(Vector3 position)
    {
        if (gameCamera == null) return false;

        // 월드 좌표를 뷰포트 좌표(0~1)로 변환
        Vector3 viewportPos = gameCamera.WorldToViewportPoint(position);

        // x, y가 0~1 사이이고, z(깊이)가 양수면 화면에 보이는 것임
        // 약간의 여유(Margin)를 두려면 -0.1f ~ 1.1f 정도로 검사해도 됨
        if (viewportPos.x > 0 && viewportPos.x < 1 &&
            viewportPos.y > 0 && viewportPos.y < 1 &&
            viewportPos.z > 0)
        {
            return true; // 화면 안임
        }

        return false; // 화면 밖임
    }

    [ContextMenu("모두 지우기")]
    public void ClearSpawnedObjects()
    {
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
            {
                if (Application.isPlaying) Destroy(obj);
                else DestroyImmediate(obj);
            }
        }
        spawnedObjects.Clear();
    }
}