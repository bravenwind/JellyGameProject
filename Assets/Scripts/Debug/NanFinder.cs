using System.Text;
using UnityEngine;

//"Invalid AABB" / "IsFinite(distanceForSort)" 의 범인을 찾는다
//두 메시지는 렌더 단계에서 나와 스택에 게임 코드가 안 찍히므로 직접 훑는다
public class NanFinder : MonoBehaviour
{
    [SerializeField] private float scanInterval = 0.5f;
    [SerializeField] private bool logOnce = true;
    [SerializeField] private bool checkClothVertices = true;

    private float timer;
    private readonly System.Collections.Generic.HashSet<int> reported = new();

    private void Update()
    {
        timer += Time.unscaledDeltaTime;

        if (timer < scanInterval)
            return;

        timer = 0f;
        Scan();
    }

    [ContextMenu("지금 검사")]
    public void Scan()
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            string reason = Diagnose(r);

            if (reason == null)
                continue;

            int id = r.GetInstanceID();

            if (logOnce && !reported.Add(id))
                continue;

            Debug.LogError($"[ {nameof(NanFinder)} ] {Path(r.transform)} : {reason}", r.gameObject);
        }
    }

    private string Diagnose(Renderer r)
    {
        Transform t = r.transform;

        if (!IsFinite(t.position))
            return $"position {t.position}";

        if (!IsFinite(t.lossyScale))
            return $"lossyScale {t.lossyScale}";

        if (!IsFinite(t.localScale))
            return $"localScale {t.localScale}";

        Quaternion q = t.rotation;
        if (!IsFinite(q.x) || !IsFinite(q.y) || !IsFinite(q.z) || !IsFinite(q.w))
            return $"rotation {q}";

        Bounds b = r.bounds;
        if (!IsFinite(b.center) || !IsFinite(b.size))
            return $"bounds center {b.center} size {b.size}";

        if (checkClothVertices)
        {
            Cloth cloth = r.GetComponent<Cloth>();

            if (cloth != null)
            {
                Vector3[] positions = cloth.vertices;

                for (int i = 0; i < positions.Length; i++)
                {
                    if (!IsFinite(positions[i]))
                        return $"Cloth 정점 {i} = {positions[i]}";
                }
            }
        }

        return null;
    }

    private static bool IsFinite(Vector3 v)
    {
        return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }

    private static bool IsFinite(float f)
    {
        return !float.IsNaN(f) && !float.IsInfinity(f);
    }

    private static string Path(Transform t)
    {
        StringBuilder sb = new(t.name);

        while (t.parent != null)
        {
            t = t.parent;
            sb.Insert(0, t.name + "/");
        }

        return sb.ToString();
    }
}
