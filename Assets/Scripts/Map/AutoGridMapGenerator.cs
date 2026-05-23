using UnityEngine;
// AI Navigation 패키지가 설치되어 있다면 아래 네임스페이스를 사용합니다.
using UnityEngine.AI;
// Unity.AI.Navigation 패키지가 없다면 using Unity.AI.Navigation; 은 필요하지 않을 수 있습니다.

#if UNITY_EDITOR
using UnityEditor; // 에디터 전용 기능을 위해 추가
#endif

public class AutoGridMapGenerator : MonoBehaviour
{
    [Header("기본")]
    public GameObject tilePrefab;
    public int width = 10;
    public int height = 10;
    public float gap = 0.0f;

    [Header("옵션")]
    public bool centerGrid = true;
    public bool addAreaCollider = true;

    [Header("NavMesh 설정")]
    [Tooltip("생성 후 자동으로 NavMesh를 베이크하기 위해 사용")]
    public bool autoBakeNavMesh = true;
    [Tooltip("생성되는 타일을 Static으로 설정하기 위해 사용 (NavMesh 필수)")]
    public bool setStatic = true;

    // AI Navigation 패키지의 NavMeshSurface 컴포넌트 참조
    // (에디터에서 직접 드래그로 연결하거나, 패키지 설치 후 주석을 해제하여 바로 사용)
    // public Unity.AI.Navigation.NavMeshSurface navMeshSurface;

    private void Start()
    {
        // GenerateGrid();
    }

    [ContextMenu("그리드 생성하기 (Generate)")]
    public void GenerateGrid()
    {
        ClearGrid();

        if (tilePrefab == null)
        {
            Debug.LogError("타일 프리팹이 없습니다.");
            return;
        }

        // 1. 프리팹 크기 계산
        Renderer prefabRenderer = tilePrefab.GetComponent<Renderer>();
        if (prefabRenderer == null) prefabRenderer = tilePrefab.GetComponentInChildren<Renderer>();

        if (prefabRenderer == null)
        {
            Debug.LogError("Renderer를 찾을 수 없습니다.");
            return;
        }

        Vector3 tileSize = prefabRenderer.bounds.size;
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

                // [추가됨] NavMesh 인식을 위한 Static 설정
                if (setStatic)
                {
                    newTile.isStatic = true;

                    // 에디터에서 실행할 때만 Navigation Static 플래그를 확인하고 설정
#if UNITY_EDITOR
                    GameObjectUtility.SetStaticEditorFlags(newTile, GameObjectUtility.GetStaticEditorFlags(newTile) | StaticEditorFlags.BatchingStatic);
#endif
                }
            }
        }

        // 3. 콜라이더 설정
        if (addAreaCollider)
        {
            ApplyColliderMath(tileSize, stepX, stepZ, centerOffsetY);
        }

        Debug.Log($"생성 완료: {width}x{height}");

        // 4. [추가됨] NavMesh 자동 베이크
        if (autoBakeNavMesh)
        {
            BakeNavMesh();
        }
    }

    private void BakeNavMesh()
    {
        // 방법 A: NavMeshSurface 컴포넌트를 사용하는 최신 방법 (권장)
        // Unity.AI.Navigation 패키지가 필요합니다.
        // 여기서는 리플렉션이나 GetComponent를 통해 유연하게 처리하거나,
        // 사용자가 직접 public NavMeshSurface surface; 필드를 연결해서 사용해야 합니다.

        // 리플렉션으로 NavMeshSurface라는 이름의 컴포넌트를 찾아 BuildNavMesh를 호출합니다.
        Component surface = GetComponent("NavMeshSurface");
        if (surface != null)
        {
            // surface.BuildNavMesh(); 는 직접 호출이 불가능하므로
            // (패키지 미설치 시 컴파일 오류가 생겨 SendMessage 방식 사용)
            surface.SendMessage("BuildNavMesh", SendMessageOptions.DontRequireReceiver);
            Debug.Log("NavMeshSurface를 통한 베이크 완료.");
        }
        else
        {
            Debug.LogWarning("NavMeshSurface 컴포넌트가 없습니다. 자동으로 베이크하지 않았습니다.\n" +
                             "GameObject에 'NavMeshSurface' 컴포넌트를 추가하세요.");
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

    [ContextMenu("그리드 초기화 (Clear)")]
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

        // NavMesh 데이터는 Clear 후 자동으로 제거되지 않으므로,
        // 다시 Generate하면 덮어씌워지므로 별도 처리는 필요 없습니다.
    }
}
