using UnityEngine;
// AI Navigation ��Ű���� ��ġ�Ǿ� �ִٸ� �Ʒ� ���ӽ����̽��� ����մϴ�.
using UnityEngine.AI;
// Unity.AI.Navigation ��Ű���� ���ٸ� using Unity.AI.Navigation; �� �ʿ��� �� �ֽ��ϴ�.

#if UNITY_EDITOR
using UnityEditor; // ������ ���� ����� ���� �߰�
#endif

public class AutoGridMapGenerator : MonoBehaviour
{
    [Header("����")]
    public GameObject tilePrefab;
    public int width = 10;
    public int height = 10;
    public float gap = 0.0f;

    [Header("�ɼ�")]
    public bool centerGrid = true;
    public bool addAreaCollider = true;

    [Header("NavMesh ����")]
    [Tooltip("���� �� �ڵ����� NavMesh�� ����ũ���� ����")]
    public bool autoBakeNavMesh = true;
    [Tooltip("������ Ÿ���� Static���� �������� ���� (NavMesh �ʼ�)")]
    public bool setStatic = true;

    // AI Navigation ��Ű���� NavMeshSurface ������Ʈ ����
    // (������ ���� ������ ���� �������� ã�ų�, ��Ű�� ��ġ �� �ּ� �����Ͽ� ��� ����)
    // public Unity.AI.Navigation.NavMeshSurface navMeshSurface; 

    private void Start()
    {
        // GenerateGrid();
    }

    [ContextMenu("���� �����ϱ� (Generate)")]
    public void GenerateGrid()
    {
        ClearGrid();

        if (tilePrefab == null)
        {
            Debug.LogError("Ÿ�� �������� �����ϴ�.");
            return;
        }

        // 1. ������ ũ�� ����
        Renderer prefabRenderer = tilePrefab.GetComponent<Renderer>();
        if (prefabRenderer == null) prefabRenderer = tilePrefab.GetComponentInChildren<Renderer>();

        if (prefabRenderer == null)
        {
            Debug.LogError("Renderer�� ã�� �� �����ϴ�.");
            return;
        }

        Vector3 tileSize = prefabRenderer.bounds.size;
        float centerOffsetY = prefabRenderer.bounds.center.y;
        float stepX = tileSize.x + gap;
        float stepZ = tileSize.z + gap;

        // 2. Ÿ�� ���� (����)
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float xPos = x * stepX;
                float zPos = z * stepZ;

                if (centerGrid)
                {
                    xPos -= (width * stepX) / 2f - (stepX / 2f);
                    zPos -= (height * stepZ) / 2f - (stepZ / 2f);
                }

                Vector3 spawnPos = new Vector3(xPos, 0, zPos) + transform.position;

                GameObject newTile = Instantiate(tilePrefab, spawnPos, Quaternion.identity);
                newTile.transform.SetParent(this.transform);
                newTile.name = $"Tile_{x}_{z}";

                // [�߰���] NavMesh �ν��� ���� Static ����
                if (setStatic)
                {
                    newTile.isStatic = true;

                    // ������ ����� ��� Navigation Static �÷��׸� Ȯ���ϰ� ����
#if UNITY_EDITOR
                    GameObjectUtility.SetStaticEditorFlags(newTile, GameObjectUtility.GetStaticEditorFlags(newTile) | StaticEditorFlags.BatchingStatic);
#endif
                }
            }
        }

        // 3. �ݶ��̴� ����
        if (addAreaCollider)
        {
            ApplyColliderMath(tileSize, stepX, stepZ, centerOffsetY);
        }

        Debug.Log($"���� �Ϸ�: {width}x{height}");

        // 4. [�߰���] NavMesh �ڵ� ����ũ
        if (autoBakeNavMesh)
        {
            BakeNavMesh();
        }
    }

    private void BakeNavMesh()
    {
        // ��� A: NavMeshSurface ������Ʈ�� ����ϴ� �ֽ� ��� (����)
        // Unity.AI.Navigation ��Ű���� �ʿ��մϴ�.
        // ���⼭�� ���÷����̳� GetComponent�� ���� �������� ó���ϰų�,
        // ����ڰ� ���� public NavMeshSurface surface; ������ �����ؼ� �����ؾ� �մϴ�.

        // ���ǻ� NavMeshSurface��� �̸��� ������Ʈ�� �پ������� BuildNavMesh�� ȣ���մϴ�.
        Component surface = GetComponent("NavMeshSurface");
        if (surface != null)
        {
            // surface.BuildNavMesh(); �� ������ ������ ����
            // (��Ű�� �̼�ġ �� ������ ������ ���� ���� SendMessage ��� ����)
            surface.SendMessage("BuildNavMesh", SendMessageOptions.DontRequireReceiver);
            Debug.Log("NavMeshSurface�� ���� ����ũ �Ϸ�.");
        }
        else
        {
            Debug.LogWarning("NavMeshSurface ������Ʈ�� �����ϴ�. �ڵ����� ����ũ���� �ʾҽ��ϴ�.\n" +
                             "GameObject�� 'NavMeshSurface' ������Ʈ�� �߰��ϼ���.");
        }
    }

    private void ApplyColliderMath(Vector3 tileSize, float stepX, float stepZ, float centerOffsetY)
    {
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol == null) boxCol = gameObject.AddComponent<BoxCollider>();

        float totalWidth = (width * stepX) - gap;
        float totalDepth = (height * stepZ) - gap;

        boxCol.size = new Vector3(totalWidth, tileSize.y, totalDepth);

        float centerX = 0f;
        float centerZ = 0f;

        if (centerGrid)
        {
            centerX = 0f;
            centerZ = 0f;
        }
        else
        {
            centerX = ((width - 1) * stepX) / 2f;
            centerZ = ((height - 1) * stepZ) / 2f;
        }

        boxCol.center = new Vector3(centerX, centerOffsetY, centerZ);
    }

    [ContextMenu("���� ����� (Clear)")]
    public void ClearGrid()
    {
        while (transform.childCount > 0)
        {
            Transform child = transform.GetChild(0);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            if (Application.isPlaying) Destroy(boxCol);
            else DestroyImmediate(boxCol);
        }

        // NavMesh �����ʹ� Clear �� �ڵ����� �������� ������, 
        // �ٽ� Generate�ϸ� ��������Ƿ� ���� ó���� �ʿ� �����ϴ�.
    }
}