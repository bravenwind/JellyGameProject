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

            bool needsWork =
                asset.GetComponentInChildren<PhotonView>(true) != null ||
                asset.GetComponentInChildren<NetworkPlayerSync>(true) != null ||
                asset.GetComponentInChildren<AIPlayerSync>(true) != null ||
                asset.GetComponentInChildren<AIPlayerMovement>(true) != null;

            if (!needsWork) { skipped++; continue; }

            List<string> changes;
            try
            {
                changes = ConvertOne(path, apply);
            }
            catch (System.Exception e)
            {
                // 한 프리팹이 실패해도 나머지는 계속 처리한다
                report.AppendLine();
                report.AppendLine("▶ " + path);
                report.AppendLine("    ★ 예외 발생: " + e.GetType().Name + " — " + e.Message);
                continue;
            }

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
            // ⓪ 깨진 스크립트 참조(Missing Script) 제거
            //    이게 하나라도 있으면 Unity가 SaveAsPrefabAsset을 거부한다.
            //    (저장하면 그 컴포넌트 정보가 영영 사라지므로 안전장치로 막는 것)
            int missing = RemoveMissingScripts(root);
            if (missing > 0)
                changes.Add("- Missing Script " + missing + "개 (저장을 막고 있던 원인)");

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

            // ③ Photon 배관 게임 스크립트 제거 → LanPlayerState
            //    이 스크립트들의 역할은 Net/ 계층으로 이미 분산 이식됐다.
            //    프리팹에 남겨두면 photonView가 null이라 런타임 에러가 쏟아진다.
            foreach (NetworkPlayerSync sync in root.GetComponentsInChildren<NetworkPlayerSync>(true))
            {
                GameObject go = sync.gameObject;
                changes.Add("- NetworkPlayerSync (" + go.name + ")");

                if (go.GetComponent<LanPlayerState>() == null)
                {
                    changes.Add("+ LanPlayerState (" + go.name + ")");
                    if (apply) go.AddComponent<LanPlayerState>();
                }
                // 로컬/원격 구성(카메라·입력·물리·Cloth)을 담당 — 없으면 조작이 아예 안 된다
                if (go.GetComponent<LanPlayerSetup>() == null)
                {
                    changes.Add("+ LanPlayerSetup (" + go.name + ")");
                    if (apply) go.AddComponent<LanPlayerSetup>();
                }
                // 크기·색·애니메이션을 기존 게임 시스템에 연결
                if (go.GetComponent<LanPlayerVisual>() == null)
                {
                    changes.Add("+ LanPlayerVisual (" + go.name + ")");
                    if (apply) go.AddComponent<LanPlayerVisual>();
                }
                if (apply) Object.DestroyImmediate(sync, true);
            }

            foreach (AIPlayerSync s in root.GetComponentsInChildren<AIPlayerSync>(true))
            {
                changes.Add("- AIPlayerSync (" + s.gameObject.name + ")");
                if (apply) Object.DestroyImmediate(s, true);
            }

            foreach (AIPlayerMovement s in root.GetComponentsInChildren<AIPlayerMovement>(true))
            {
                changes.Add("- AIPlayerMovement (" + s.gameObject.name + ")  ※ 봇은 이후 단계에서 재구현");
                if (apply) Object.DestroyImmediate(s, true);
            }

            // ④ PhotonView 제거 → NetIdentity + NetScale
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
            {
                bool ok;
                PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
                if (!ok) changes.Add("★ 저장 실패 — 변경이 반영되지 않았습니다");
                else changes.Add("저장 완료");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return changes;
    }

    /// <summary>
    /// 플레이어 프리팹(= PlayerMovement 보유)에 네트워크 컴포넌트를 보장한다.
    ///
    /// ★ 왜 따로 필요한가
    ///   변환기의 다른 단계는 "NetworkPlayerSync가 있으면"을 조건으로 삼는데,
    ///   그건 첫 변환에서 이미 지워진다. 그래서 두 번째부터는 그 블록이 아예 안 돌아
    ///   나중에 추가한 컴포넌트(LanPlayerVisual 등)가 영영 안 붙는다. (실제로 겪음)
    ///   이 메뉴는 그 조건과 무관하게 '플레이어인가'만 보고 보정한다.
    /// </summary>
    [MenuItem("Tools/LAN 이식/⑥ 플레이어 컴포넌트 보정", false, 6)]
    public static void FixPlayerComponents()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder sb = new StringBuilder("=== 플레이어 컴포넌트 보정 ===\n");
        int fixedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            if (asset.GetComponent<NetIdentity>() == null) continue;
            if (asset.GetComponentInChildren<PlayerMovement>(true) == null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                List<string> added = new List<string>();

                RemoveMissingScripts(root);
                Need<LanPlayerSetup>(root, added);
                Need<LanPlayerVisual>(root, added);
                Need<LanPlayerState>(root, added);
                Need<NetTransform>(root, added);
                Need<NetKnockback>(root, added);
                Need<NetScale>(root, added);

                if (added.Count == 0) { sb.AppendLine("▶ " + path + "  — 이미 완비"); continue; }

                bool ok;
                PrefabUtility.SaveAsPrefabAsset(root, path, out ok);

                sb.AppendLine("▶ " + path);
                foreach (string a in added) sb.AppendLine("    + " + a);
                sb.AppendLine("    " + (ok ? "저장 완료" : "★ 저장 실패"));
                if (ok) fixedCount++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        sb.AppendLine();
        sb.AppendLine(fixedCount + "개 프리팹 보정됨.");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// AI 봇 프리팹을 LAN 구성으로 맞춘다.
    ///
    /// ★ 봇에 필요한 것과 그 이유
    ///   NetIdentity   — 누구의 것인지(= 호스트 소유). 없으면 봇이 네트워크 오브젝트가 아니다
    ///   NetTransform  — 호스트가 굴린 결과 위치를 나머지에게 전달
    ///   LanBotSync    — 크기 스트림 + 탈락 통보 (AIPlayerSync 대체)
    ///   LanPlayerVisual — 애니메이션(IsMoving/Dash/Attack)을 플레이어와 같은 통로로
    ///   NetKnockback  — 배트에 맞았을 때 밀려나기 (밀치기 모드)
    ///
    ///   LanPlayerState는 <b>붙이지 않는다.</b> 그걸 붙이면 봇이
    ///   EntityRegistry.Players에 등록돼 사람 플레이어로 집계된다.
    /// </summary>
    [MenuItem("Tools/LAN 이식/⑨ AI 봇 프리팹 보정", false, 9)]
    public static void FixBotPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder sb = new StringBuilder("=== AI 봇 프리팹 보정 ===\n");
        int fixedCount = 0, found = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            // ★ AIPlayerMovement로 찾으면 안 된다 — 변환 과정에서 그게 지워졌기 때문에
            //   찾을 대상이 0개가 된다(실제로 그랬다). 봇에만 있고 살아남은
            //   AIDetector를 표식으로 쓴다.
            if (asset.GetComponentInChildren<AIDetector>(true) == null) continue;

            found++;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                List<string> added = new List<string>();

                RemoveMissingScripts(root);
                Need<NetIdentity>(root, added);
                Need<NetTransform>(root, added);
                Need<AIPlayerMovement>(root, added);   // 변환 때 지워진 두뇌를 되살린다
                Need<LanBotSync>(root, added);
                Need<LanPlayerVisual>(root, added);
                Need<NetKnockback>(root, added);

                // 이름표 참조를 붙여준다(원본은 인스펙터로 연결돼 있었다)
                AIPlayerMovement ai = root.GetComponent<AIPlayerMovement>();
                if (ai != null && ai.nameTagBillboard == null)
                {
                    ai.nameTagBillboard = root.GetComponentInChildren<NameTagBillboard>(true);
                    if (ai.nameTagBillboard != null) added.Add("nameTagBillboard 연결");
                }

                if (root.GetComponent<LanPlayerState>() != null)
                {
                    Object.DestroyImmediate(root.GetComponent<LanPlayerState>(), true);
                    added.Add("- LanPlayerState (봇은 사람 목록에 들어가면 안 됨)");
                }

                if (added.Count == 0) { sb.AppendLine("▶ " + path + "  — 이미 완비"); continue; }

                bool ok;
                PrefabUtility.SaveAsPrefabAsset(root, path, out ok);

                sb.AppendLine("▶ " + path);
                foreach (string a in added) sb.AppendLine("    " + (a.StartsWith("-") ? a : "+ " + a));
                sb.AppendLine("    " + (ok ? "저장 완료" : "★ 저장 실패"));
                if (ok) fixedCount++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        sb.AppendLine();
        if (found == 0)
            sb.AppendLine("★ AIDetector를 가진 봇 프리팹을 못 찾았습니다. "
                          + "SearchFolders 밖에 있는지 확인해주세요.");
        else
            sb.AppendLine(found + "개 중 " + fixedCount + "개 보정됨. "
                          + "이제 NetWorld.prefabs에 이 프리팹을 등록하고 "
                          + "그 인덱스를 LanBotSpawner.botPrefabId에 적어주세요.");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 움직이는 젤리(NavMeshAgent 보유)에 NetTransform을 붙인다.
    ///
    /// ★ 왜 필요한가
    ///   Wandering/Patrol 젤리는 스스로 돌아다닌다. 호스트에서만 AI를 돌리고
    ///   그 위치를 나머지에게 보내야 모두가 같은 자리에서 본다.
    ///   NetTransform이 없으면 각 클라의 젤리가 제각각 흩어진다.
    /// </summary>
    [MenuItem("Tools/LAN 이식/⑧ 움직이는 젤리에 NetTransform 부여", false, 8)]
    public static void FixMovingJellies()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder sb = new StringBuilder("=== 움직이는 젤리 보정 ===\n");
        int n = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            if (asset.GetComponent<NetIdentity>() == null) continue;
            if (asset.GetComponentInChildren<PlayerMovement>(true) != null) continue;   // 플레이어 제외
            if (asset.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) == null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                List<string> added = new List<string>();
                RemoveMissingScripts(root);
                Need<NetTransform>(root, added);
                Need<NetScale>(root, added);

                if (added.Count == 0) { sb.AppendLine("▶ " + asset.name + " — 이미 완비"); continue; }

                bool ok;
                PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
                sb.AppendLine("▶ " + asset.name + "  + " + string.Join(", ", added)
                              + "  " + (ok ? "✓" : "★저장실패"));
                if (ok) n++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        sb.AppendLine();
        sb.AppendLine(n + "개 보정됨.");
        Debug.Log(sb.ToString());
    }

    static void Need<T>(GameObject root, List<string> added) where T : Component
    {
        if (root.GetComponent<T>() != null) return;
        root.AddComponent<T>();
        added.Add(typeof(T).Name);
    }

    /// <summary>
    /// 깨진 스크립트 참조를 전부 제거하고 개수를 돌려준다(자식 포함).
    /// 삭제된 스크립트를 가리키던 컴포넌트로, 어차피 아무 동작도 하지 않는다.
    /// </summary>
    static int RemoveMissingScripts(GameObject root)
    {
        int total = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            total += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        return total;
    }

    /// <summary>프리팹 전체에서 Missing Script만 정리한다(변환과 별개로 쓸 수 있게).</summary>
    [MenuItem("Tools/LAN 이식/Missing Script 정리", false, 21)]
    public static void CleanMissingScripts()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder sb = new StringBuilder("=== Missing Script 정리 ===\n");
        int totalPrefabs = 0, totalComps = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = RemoveMissingScripts(root);
                if (n == 0) continue;

                bool ok;
                PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
                sb.AppendLine("▶ " + path + "  — " + n + "개 제거 " + (ok ? "✓" : "★저장실패"));
                totalPrefabs++; totalComps += n;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        sb.AppendLine();
        sb.AppendLine(totalPrefabs == 0
            ? "깨진 참조가 없습니다."
            : "프리팹 " + totalPrefabs + "개에서 " + totalComps + "개 제거.");
        Debug.Log(sb.ToString());
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

    /// <summary>
    /// 남은 프리팹을 '에셋 직접 편집' 방식으로 처리한다.
    ///
    /// LoadPrefabContents → SaveAsPrefabAsset 경로가 실패하는 프리팹이 있다.
    /// (중첩 프리팹이 많거나 임시 씬 로드 중 컴포넌트가 예외를 던지는 경우)
    /// 그럴 땐 에셋을 직접 열어 고치고 SetDirty + SaveAssets로 저장하는 편이 확실하다.
    /// </summary>
    [MenuItem("Tools/LAN 이식/⑤ 남은 것 강제 변환", false, 5)]
    public static void ForceConvertRemaining()
    {
        if (!EditorUtility.DisplayDialog("강제 변환",
                "일반 변환이 실패한 프리팹을 에셋 직접 편집 방식으로 처리합니다.\n실행할까요?",
                "실행", "취소")) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchFolders);
        StringBuilder sb = new StringBuilder("=== 강제 변환 ===\n");
        int done = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            if (asset.GetComponentInChildren<PhotonView>(true) == null) continue;

            sb.AppendLine();
            sb.AppendLine("▶ " + path);

            try
            {
                // 루트에 붙은 것만 처리한다(중첩 자식은 그 원본 프리팹에서 처리됨)
                StripAndAdd<PhotonTransformView>(asset, sb, typeof(NetTransform), typeof(NetKnockback));
                StripAndAdd<NetworkPlayerSync>(asset, sb, typeof(LanPlayerState));
                StripAndAdd<AIPlayerSync>(asset, sb);
                StripAndAdd<AIPlayerMovement>(asset, sb);
                StripAndAdd<PhotonView>(asset, sb, typeof(NetIdentity), typeof(NetScale));

                EditorUtility.SetDirty(asset);
                done++;
            }
            catch (System.Exception e)
            {
                sb.AppendLine("    ★ 예외: " + e.GetType().Name + " — " + e.Message);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        sb.AppendLine();
        sb.AppendLine(done + "개 처리. [현황 조사]로 확인하세요.");
        Debug.Log(sb.ToString());
    }

    /// <summary>컴포넌트 T를 지우고 대체 컴포넌트들을 붙인다(에셋 직접 편집).</summary>
    static void StripAndAdd<T>(GameObject asset, StringBuilder sb, params System.Type[] adds)
        where T : Component
    {
        T[] found = asset.GetComponentsInChildren<T>(true);
        if (found.Length == 0) return;

        foreach (T comp in found)
        {
            GameObject go = comp.gameObject;

            foreach (System.Type add in adds)
            {
                if (go.GetComponent(add) != null) continue;
                go.AddComponent(add);
                sb.AppendLine("    + " + add.Name + " (" + go.name + ")");
            }

            sb.AppendLine("    - " + typeof(T).Name + " (" + go.name + ")");
            Object.DestroyImmediate(comp, true);   // allowDestroyingAssets: true
        }
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
