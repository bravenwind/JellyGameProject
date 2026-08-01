using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Photon.Pun;
using JellyNet;

/// <summary>
/// 프리팹에 붙은 Photon 컴포넌트를 자체 Net 컴포넌트로 일괄 교체한다.
///
///   PhotonView          → NetIdentity
///   PhotonTransformView → NetTransform  (+ NetKnockback)
///   (모든 대상)         → NetScale
///
/// ★ 반드시 커밋한 뒤에 실행할 것. 프리팹 29개를 한 번에 고친다.
/// ★ 먼저 [① 미리보기]로 무엇이 바뀔지 확인하고 [② 실행]을 누른다.
///
/// 메뉴: Tools ▸ LAN 이식
/// </summary>
public static class PhotonToNetConverter
{
    // Photon 데모 프리팹은 건드리지 않는다
    static readonly string[] SearchFolders = { "Assets/Prefabs", "Assets/Resources" };

    [MenuItem("Tools/LAN 이식/① 프리팹 변환 미리보기", false, 1)]
    public static void Preview()
    {
        Run(false);
    }

    [MenuItem("Tools/LAN 이식/② 프리팹 변환 실행", false, 2)]
    public static void Apply()
    {
        bool ok = EditorUtility.DisplayDialog(
            "프리팹 일괄 변환",
            "Photon 컴포넌트를 Net 컴포넌트로 교체합니다.\n\n" +
            "· 되돌리기 어려우니 반드시 커밋 후 실행하세요.\n" +
            "· 먼저 [① 미리보기]로 확인하셨나요?",
            "실행", "취소");

        if (ok) Run(true);
    }

    // ─────────────────────────────────────────────
    static void Run(bool apply)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder report = new StringBuilder();
        int touched = 0, skipped = 0;

        report.AppendLine(apply ? "=== 프리팹 변환 실행 ===" : "=== 프리팹 변환 미리보기 (변경 없음) ===");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // 원본 에셋을 열지 않고 먼저 훑어서 대상인지만 확인
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            if (asset.GetComponentInChildren<PhotonView>(true) == null) { skipped++; continue; }

            List<string> changes = ConvertOne(path, apply);
            if (changes.Count == 0) { skipped++; continue; }

            touched++;
            report.AppendLine();
            report.AppendLine("▶ " + path);
            foreach (string c in changes) report.AppendLine("    " + c);
        }

        report.AppendLine();
        report.AppendLine("대상 " + touched + "개 / 건너뜀 " + skipped + "개");

        if (apply)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.AppendLine("→ 저장 완료. Unity가 다시 임포트합니다.");
        }
        else
        {
            report.AppendLine("→ 미리보기였습니다. 실제 변경은 [② 실행].");
        }

        Debug.Log(report.ToString());
    }

    /// <summary>프리팹 하나를 변환한다. apply=false면 무엇이 바뀔지만 알려준다.</summary>
    static List<string> ConvertOne(string path, bool apply)
    {
        List<string> changes = new List<string>();

        // 프리팹 내용을 임시 씬으로 열어야 자식까지 안전하게 고칠 수 있다
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            PhotonView[] views = root.GetComponentsInChildren<PhotonView>(true);
            PhotonTransformView[] tviews = root.GetComponentsInChildren<PhotonTransformView>(true);

            // ① 움직이는 오브젝트 표시 (PhotonTransformView가 있으면 위치 동기화 대상)
            HashSet<GameObject> movers = new HashSet<GameObject>();
            foreach (PhotonTransformView tv in tviews) movers.Add(tv.gameObject);

            // ② PhotonTransformView 제거 → NetTransform + NetKnockback
            foreach (PhotonTransformView tv in tviews)
            {
                GameObject go = tv.gameObject;
                changes.Add("- PhotonTransformView (" + go.name + ")");

                if (go.GetComponent<NetTransform>() == null)
                {
                    changes.Add("+ NetTransform (" + go.name + ")");
                    if (apply) go.AddComponent<NetTransform>();
                }
                if (go.GetComponent<NetKnockback>() == null)
                {
                    changes.Add("+ NetKnockback (" + go.name + ")");
                    if (apply) go.AddComponent<NetKnockback>();
                }
                if (apply) Object.DestroyImmediate(tv, true);
            }

            // ③ PhotonView 제거 → NetIdentity + NetScale
            foreach (PhotonView pv in views)
            {
                GameObject go = pv.gameObject;
                changes.Add("- PhotonView (" + go.name + ")");

                if (go.GetComponent<NetIdentity>() == null)
                {
                    changes.Add("+ NetIdentity (" + go.name + ")");
                    if (apply) go.AddComponent<NetIdentity>();
                }
                if (go.GetComponent<NetScale>() == null)
                {
                    changes.Add("+ NetScale (" + go.name + ")");
                    if (apply) go.AddComponent<NetScale>();
                }
                if (apply) Object.DestroyImmediate(pv, true);
            }

            if (apply && changes.Count > 0)
                PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return changes;
    }

    // ─────────────────────────────────────────────
    /// <summary>
    /// 아직 PhotonView가 남은 프리팹을 정확히 진단한다.
    /// 어느 오브젝트에, 어떤 형태(배리언트/중첩)로 남았는지 알려준다.
    /// </summary>
    [MenuItem("Tools/LAN 이식/③ 잔여 진단", false, 3)]
    public static void Diagnose()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder sb = new StringBuilder("=== 잔여 PhotonView 진단 ===\n");
        int found = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            if (asset.GetComponentInChildren<PhotonView>(true) == null) continue;

            found++;
            sb.AppendLine();
            sb.AppendLine("▶ " + path);

            // 이 프리팹이 다른 프리팹의 배리언트인가?
            PrefabAssetType kind = PrefabUtility.GetPrefabAssetType(asset);
            sb.AppendLine("    종류: " + kind);

            if (kind == PrefabAssetType.Variant)
            {
                Object baseAsset = PrefabUtility.GetCorrespondingObjectFromSource(asset);
                sb.AppendLine("    ★ 배리언트입니다. 원본: " +
                    (baseAsset != null ? AssetDatabase.GetAssetPath(baseAsset) : "?"));
            }

            foreach (PhotonView pv in asset.GetComponentInChildren<Transform>(true)
                         .GetComponentsInChildren<PhotonView>(true))
            {
                GameObject go = pv.gameObject;
                sb.AppendLine("    · PhotonView 위치: " + GetPath(go, asset.transform));

                // 이 오브젝트가 다른 프리팹에서 온 것인가(중첩)?
                GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (src != null && src != go)
                {
                    string srcPath = AssetDatabase.GetAssetPath(src);
                    if (!string.IsNullOrEmpty(srcPath) && srcPath != path)
                        sb.AppendLine("      ★ 중첩 프리팹에서 상속됨 → 원본을 먼저 고쳐야 함: " + srcPath);
                }

                bool hasNet = go.GetComponent<NetIdentity>() != null;
                sb.AppendLine("      NetIdentity " + (hasNet ? "있음(둘 다 상태)" : "없음"));
            }
        }

        if (found == 0) sb.AppendLine("\n남은 것이 없습니다. 변환 완료.");
        else sb.AppendLine("\n총 " + found + "개 프리팹에 PhotonView가 남아 있습니다.");

        Debug.Log(sb.ToString());
    }

    static string GetPath(GameObject go, Transform root)
    {
        string p = go.name;
        Transform t = go.transform.parent;
        while (t != null && t != root) { p = t.name + "/" + p; t = t.parent; }
        return p;
    }

    // ─────────────────────────────────────────────
    [MenuItem("Tools/LAN 이식/현황 조사 (변경 없음)", false, 20)]
    public static void Survey()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder sb = new StringBuilder("=== 프리팹 현황 ===\n");

        int withPhoton = 0, withNet = 0, both = 0, neither = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            bool p = go.GetComponentInChildren<PhotonView>(true) != null;
            bool n = go.GetComponentInChildren<NetIdentity>(true) != null;

            if (p && n) { both++; sb.AppendLine("[둘 다] " + path); }
            else if (p) withPhoton++;
            else if (n) withNet++;
            else neither++;
        }

        sb.AppendLine();
        sb.AppendLine("Photon만  : " + withPhoton + "개  ← 변환 대상");
        sb.AppendLine("Net만     : " + withNet + "개  ← 변환 완료");
        sb.AppendLine("둘 다     : " + both + "개  ← 확인 필요");
        sb.AppendLine("해당 없음 : " + neither + "개");

        Debug.Log(sb.ToString());
    }
}
