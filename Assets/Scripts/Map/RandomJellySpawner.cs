using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class RandomJellySpawner : MonoBehaviour
{
    [Header("�ʼ� ����")]
    public AutoGridMapGenerator mapGenerator; // �� ������
    public GameObject[] objectPrefab;           // ��ȯ�� ������Ʈ
    public Camera gameCamera;                 // ���� ���� ī�޶� (������ �ڵ� �Ҵ�)

    [Header("���� ����")]
    public int maxObjectCount = 200;          // �ִ� ������Ʈ ����
    public float spawnInterval = 2.0f;        // ���� ���� (��)
    public float yOffset = 1.0f;              // ���� ����

    [Header("�����")]
    public bool isSpawning = true;            // ���� Ȱ��ȭ ����

    [Header("��ġ ����")]
    public int batchSpawnCount = 100; // �� ���� ��ġ�� ����

    // ���� ����Ʈ
    private List<GameObject> spawnedObjects = new List<GameObject>();

    private void Start()
    {
        if (PhotonNetwork.InRoom)
        {
            enabled = false;
            return;
        }

        // ī�޶� ���� �� �Ǿ� ������ ���� ī�޶� ã��
        if (gameCamera == null) gameCamera = Camera.main;

        // �ڵ� ���� �ڷ�ƾ ����
        StartCoroutine(AutoSpawnRoutine());
    }

    // ���� �������� �����ϴ� �ڷ�ƾ
    IEnumerator AutoSpawnRoutine()
    {
        while (true)
        {
            // 1. ���� ���
            yield return new WaitForSeconds(spawnInterval);

            if (!isSpawning) continue;

            // 2. ����Ʈ û�� (�ı��� ������Ʈ ����)
            // ����Ʈ ��� �� null�� ��(�̹� ���ӿ��� ������ ��)�� ��� ����
            spawnedObjects.RemoveAll(item => item == null);

            // 3. �ִ� ���� üũ
            if (spawnedObjects.Count >= maxObjectCount)
            {
                // �� á���� ���� ��ŵ
                continue;
            }

            // 4. ī�޶� �� ��ġ ã�� �� ���� �õ�
            TrySpawnOffScreen();
        }
    }

    [ContextMenu("���� 100�� ��� ��ġ")]
    public void SpawnRandomBatchDefault()
    {
        SpawnRandomBatch(batchSpawnCount);
    }

    /// <summary>
    /// �־��� ������ŭ �� ���� ���� �����ϰ� ������Ʈ�� ��ġ�մϴ�. (ī�޶� ���� ���� �������)
    /// </summary>
    public void SpawnRandomBatch(int count)
    {
        if (mapGenerator == null || objectPrefab == null || objectPrefab.Length == 0)
        {
            Debug.LogWarning("RandomJellySpawner: �ʼ� ���� ��Ұ� �����Ǿ����ϴ�.");
            return;
        }

        // 1. �� ũ�� �� ���� ��� (���� TrySpawnOffScreen ���� Ȱ��)
        Renderer tileRenderer = mapGenerator.tilePrefab.GetComponent<Renderer>();
        if (tileRenderer == null) tileRenderer = mapGenerator.tilePrefab.GetComponentInChildren<Renderer>();

        Vector3 tileSize = tileRenderer.bounds.size;
        float stepX = tileSize.x + mapGenerator.gap;
        float stepZ = tileSize.z + mapGenerator.gap;
        float mapSizeX = (mapGenerator.width - 1) * stepX;
        float mapSizeZ = (mapGenerator.height - 1) * stepZ;

        // 2. ������ ������ŭ �ݺ� ����
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

            // 3. ������ ���� ���� �� ����
            int jellyIndex = Random.Range(0, objectPrefab.Length);
            GameObject obj = Instantiate(objectPrefab[jellyIndex], spawnPos, Quaternion.identity);

            // 4. ���� ����
            obj.transform.SetParent(this.transform);
            obj.name = $"{objectPrefab[jellyIndex].name}_Batch_{System.DateTime.Now.Ticks}_{i}";

            spawnedObjects.Add(obj);
        }

        Debug.Log($"RandomJellySpawner: {count}���� ������Ʈ�� ��ġ�߽��ϴ�.");
    }

    void TrySpawnOffScreen()
    {
        if (mapGenerator == null || objectPrefab == null) return;

        // �� ũ�� ��� (�Ź� ����ϰų�, Start���� ĳ���ص� ��)
        // ���⼭�� �����ϰ� �Ź� ���
        Renderer tileRenderer = mapGenerator.tilePrefab.GetComponent<Renderer>();
        if (tileRenderer == null) tileRenderer = mapGenerator.tilePrefab.GetComponentInChildren<Renderer>();

        Vector3 tileSize = tileRenderer.bounds.size;
        float stepX = tileSize.x + mapGenerator.gap;
        float stepZ = tileSize.z + mapGenerator.gap;
        float mapSizeX = (mapGenerator.width - 1) * stepX;
        float mapSizeZ = (mapGenerator.height - 1) * stepZ;

        // ��ȿ�� ��ġ�� ã�� ������ �� �� �õ����� (���� ���� ����)
        int maxAttempts = 10;
        Vector3 finalPos = Vector3.zero;
        bool foundValidPos = false;

        for (int i = 0; i < maxAttempts; i++)
        {
            // ���� ��ġ ���
            float rX = Random.Range(0f, mapSizeX);
            float rZ = Random.Range(0f, mapSizeZ);

            if (mapGenerator.centerGrid)
            {
                rX -= mapSizeX / 2f;
                rZ -= mapSizeZ / 2f;
            }

            Vector3 candidatePos = mapGenerator.transform.position + new Vector3(rX, yOffset, rZ);

            // ** �ٽ�: ī�޶� ȭ�� �ȿ� �ִ��� �˻� **
            if (!IsVisibleFromCamera(candidatePos))
            {
                finalPos = candidatePos;
                foundValidPos = true;
                break; // ȭ�� �� ��ġ�� ã������ ���� Ż��
            }
        }

        // ��ȿ�� ��ġ�� ã�Ҵٸ� ������Ʈ ����
        if (foundValidPos)
        {
            int jellyIndex = Random.Range(0, objectPrefab.Length);
            GameObject obj = Instantiate(objectPrefab[jellyIndex], finalPos, Quaternion.identity);

            obj.transform.SetParent(this.transform);
            obj.name = $"{objectPrefab[jellyIndex].name}_Auto_{Time.time}";

            spawnedObjects.Add(obj);
        }
    }

    // ��ġ�� ī�޶� ȭ�� �ȿ� �������� Ȯ���ϴ� �Լ�
    bool IsVisibleFromCamera(Vector3 position)
    {
        if (gameCamera == null) return false;

        // ���� ��ǥ�� ����Ʈ ��ǥ(0~1)�� ��ȯ
        Vector3 viewportPos = gameCamera.WorldToViewportPoint(position);

        // x, y�� 0~1 �����̰�, z(����)�� ����� ȭ�鿡 ���̴� ����
        // �ణ�� ����(Margin)�� �η��� -0.1f ~ 1.1f ������ �˻��ص� ��
        if (viewportPos.x > 0 && viewportPos.x < 1 &&
            viewportPos.y > 0 && viewportPos.y < 1 &&
            viewportPos.z > 0)
        {
            return true; // ȭ�� ����
        }

        return false; // ȭ�� ����
    }

    [ContextMenu("��� �����")]
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