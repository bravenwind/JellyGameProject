using UnityEngine;

public class AutoGridMapGenerator : MonoBehaviour
{
    [Header("설정")]
    public GameObject tilePrefab;
    public int width = 10;
    public int height = 10;
    public float gap = 0.0f;

    [Header("옵션")]
    public bool centerGrid = true;
    public bool addAreaCollider = true;

    
    private void Start()
    {
        //GenerateGrid();
    }

    [ContextMenu("지형 생성하기 (Generate)")]
    public void GenerateGrid()
    {
        ClearGrid();

        if (tilePrefab == null)
        {
            Debug.LogError("타일 프리팹이 없습니다.");
            return;
        }

        // 1. 프리팹 크기 측정
        Renderer prefabRenderer = tilePrefab.GetComponent<Renderer>();
        if (prefabRenderer == null) prefabRenderer = tilePrefab.GetComponentInChildren<Renderer>();

        if (prefabRenderer == null)
        {
            Debug.LogError("Renderer를 찾을 수 없습니다.");
            return;
        }

        Vector3 tileSize = prefabRenderer.bounds.size;

        // 프리팹의 중심점 높이(Y) 오프셋 (피벗이 바닥인지 중앙인지에 따라 대응)
        float centerOffsetY = prefabRenderer.bounds.center.y;

        float stepX = tileSize.x + gap;
        float stepZ = tileSize.z + gap;

        // 2. 타일 생성 (루프)
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
            }
        }

        // 3. [최적화됨] 계산을 통한 콜라이더 적용 (루프 없음)
        if (addAreaCollider)
        {
            ApplyColliderMath(tileSize, stepX, stepZ, centerOffsetY);
        }

        Debug.Log($"생성 완료: {width}x{height} (타일 크기: {tileSize})");
    }

    // 수학적 계산으로 콜라이더를 씌우는 함수 (O(1) 성능)
    private void ApplyColliderMath(Vector3 tileSize, float stepX, float stepZ, float centerOffsetY)
    {
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol == null) boxCol = gameObject.AddComponent<BoxCollider>();

        // 1. 전체 사이즈 계산
        // (개수 * 간격) - 마지막 간격(gap) 하나 뺌
        float totalWidth = (width * stepX) - gap;
        float totalDepth = (height * stepZ) - gap;

        boxCol.size = new Vector3(totalWidth, tileSize.y, totalDepth);

        // 2. 중심점(Center) 계산
        float centerX = 0f;
        float centerZ = 0f;

        if (centerGrid)
        {
            // 중앙 정렬 상태면 중심은 (0,0)입니다.
            centerX = 0f;
            centerZ = 0f;
        }
        else
        {
            // 중앙 정렬이 아니면, 0부터 시작해서 양수 방향으로 뻗어나갑니다.
            // 0번째 타일(0) ~ 마지막 타일((N-1)*step) 의 중간 지점
            centerX = ((width - 1) * stepX) / 2f;
            centerZ = ((height - 1) * stepZ) / 2f;
        }

        // Y축 중심은 프리팹 자체의 중심점 오프셋을 따릅니다.
        boxCol.center = new Vector3(centerX, centerOffsetY, centerZ);
    }

    [ContextMenu("지형 지우기 (Clear)")]
    public void ClearGrid()
    {
        while (transform.childCount > 0)
        {
            Transform child = transform.GetChild(0);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        // 콜라이더도 함께 제거 (선택)
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            if (Application.isPlaying) Destroy(boxCol);
            else DestroyImmediate(boxCol);
        }
    }
}