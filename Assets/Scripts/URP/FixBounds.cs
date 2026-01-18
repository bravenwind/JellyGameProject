using UnityEngine;

// 이 스크립트를 젤리 오브젝트에 추가하세요!
public class FixBounds : MonoBehaviour
{
    void Start()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        if (mesh != null)
        {
            // 메쉬의 인식 범위를 강제로 엄청 크게 늘림
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000.0f);
        }
    }
}