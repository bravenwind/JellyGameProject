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
        // SpawnObjects(); // 게임 시작 시 자동 생성하려면 주석 해제
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

        // 2. 그냥 횟수만큼 돌면서 무조건 생성 (중복 체크 X)
        for (int i = 0; i < spawnCount; i++)
        {
            int jellyIndex = Random.Range(0, objectPrefab.Length);

            // 랜덤 인덱스 뽑기
            int rX = Random.Range(0, mapGenerator.width);
            int rZ = Random.Range(0, mapGenerator.height);

            // 위치 계산 (AutoGridMapGenerator 공식)
            float xPos = rX * stepX;
            float zPos = rZ * stepZ;

            // 중앙 정렬 보정
            if (mapGenerator.centerGrid)
            {
                xPos -= (mapGenerator.width * stepX) / 2f - (stepX / 2f);
                zPos -= (mapGenerator.height * stepZ) / 2f - (stepZ / 2f);
            }

            Vector3 spawnPos = mapGenerator.transform.position + new Vector3(xPos, yOffset, zPos);

            // 생성
            GameObject obj = Instantiate(objectPrefab[jellyIndex], spawnPos, Quaternion.identity);
            obj.transform.SetParent(this.transform);
            obj.name = $"{objectPrefab[jellyIndex].name}_{i}"; // 이름은 그냥 순번으로

            spawnedObjects.Add(obj);
        }

        Debug.Log($"중복 허용 랜덤 소환 완료: {spawnCount}개");
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