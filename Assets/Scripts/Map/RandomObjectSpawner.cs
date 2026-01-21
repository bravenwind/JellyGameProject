using System.Collections.Generic;
using UnityEngine;

public class RandomObjectSpawner : MonoBehaviour
{
    [Header("필수 연결")]
    public AutoGridMapGenerator mapGenerator; // 맵 생성기
    public GameObject[] objectPrefab;           // 소환할 오브젝트

    [Header("생성 설정")]
    public int spawnCount = 10;               // 소환할 개수
    public float yOffset = 1.0f;              // 높이 조절

    // 나중에 지우기 위한 리스트
    private List<GameObject> spawnedObjects = new List<GameObject>();

    private void Start()
    {
        // SpawnObjects(); 
    }

    [ContextMenu("무조건 랜덤 소환 (Spawn)")]
    public void SpawnObjects()
    {
        if (mapGenerator == null || objectPrefab == null || mapGenerator.tilePrefab == null)
        {
            Debug.LogError("맵 생성기 또는 프리팹 연결을 확인해주세요.");
            return;
        }

        ClearSpawnedObjects();

        // 1. 위치 계산을 위한 치수 측정
        Renderer tileRenderer = mapGenerator.tilePrefab.GetComponent<Renderer>();
        if (tileRenderer == null) tileRenderer = mapGenerator.tilePrefab.GetComponentInChildren<Renderer>();

        Vector3 tileSize = tileRenderer.bounds.size;
        float stepX = tileSize.x + mapGenerator.gap;
        float stepZ = tileSize.z + mapGenerator.gap;

        // 전체 맵의 실제 물리적 크기 계산 (첫 타일 중심 ~ 끝 타일 중심)
        // (width - 1)을 하는 이유는 타일 개수가 5개면 간격은 4칸이기 때문
        float mapSizeX = (mapGenerator.width - 1) * stepX;
        float mapSizeZ = (mapGenerator.height - 1) * stepZ;

        // 2. 횟수만큼 완전 무작위 위치 생성
        for (int i = 0; i < spawnCount; i++)
        {
            int jellyIndex = Random.Range(0, objectPrefab.Length);

            // [변경점] int(정수) 인덱스가 아니라 float(실수) 범위 내에서 랜덤 추출
            // 0 ~ 맵 전체 길이 사이의 아무 실수값이나 뽑음
            float rX = Random.Range(0f, mapSizeX);
            float rZ = Random.Range(0f, mapSizeZ);

            // 중앙 정렬 보정 로직
            if (mapGenerator.centerGrid)
            {
                // 0 ~ Max 범위를 -Half ~ +Half 범위로 이동
                rX -= mapSizeX / 2f;
                rZ -= mapSizeZ / 2f;
            }

            Vector3 spawnPos = mapGenerator.transform.position + new Vector3(rX, yOffset, rZ);

            // 생성
            GameObject obj = Instantiate(objectPrefab[jellyIndex], spawnPos, Quaternion.identity);
            obj.transform.SetParent(this.transform);
            obj.name = $"{objectPrefab[jellyIndex].name}_{i}";

            spawnedObjects.Add(obj);
        }

        Debug.Log($"자유 위치 랜덤 소환 완료: {spawnCount}개");
    }

    [ContextMenu("지우기 (Clear)")]
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