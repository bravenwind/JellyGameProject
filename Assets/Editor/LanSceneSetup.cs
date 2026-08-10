using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using JellyNet;

/// <summary>
/// 열려 있는 씬을 LAN(소켓) 구성으로 바꾼다.
///
///   ① Photon 매니저 오브젝트를 비활성화한다(삭제하지 않음 — 되돌리기 쉽게)
///   ② LanNet 오브젝트를 만들고 NetManager · NetWorld · AbsorbMode · PushMode ·
///      LanGameFlow · NetTestUI 를 붙인다
///   ③ NetWorld.prefabs 를 자동으로 채운다 (0번 플레이어, 1번부터 젤리)
///
/// 메뉴: Tools ▸ LAN 이식 ▸ ④ 현재 씬을 LAN 구성으로
/// </summary>
public static class LanSceneSetup
{
    // 씬에서 꺼야 할 Photon 매니저들 (타입 이름으로 찾는다)
    static readonly string[] PhotonManagerTypes =
    {
        "NetworkManager", "GameModeManager", "NetworkJellyManager",
        "LobbyController", "AutoConnectForTest", "NetworkNavMeshHelper"
    };

    const string PlayerPrefabPath = "Assets/Prefabs/PlayerJellies/NetworkPlayer.prefab";
    const string JellyFolder = "Assets/Resources/Prefabs";

    static bool forceRefillPrefabs = false;

    /// <summary>
    /// 씬에 미리 배치된 NetIdentity에 고정 ID를 부여한다.
    ///
    /// ★ 왜 필요한가
    ///   씬에 깔아둔 젤리 수백 개는 호스트가 스폰하지 않는다. 양쪽이 각자 씬에서 로드하므로
    ///   "내 화면의 이 젤리 = 상대 화면의 저 젤리"를 이어줄 공통 번호가 필요하다.
    ///   Photon이 씬 배치 PhotonView에 고정 ViewID를 주는 것과 같은 원리다.
    ///
    ///   이걸 안 하면 씬 젤리의 netId가 0이라 흡수 요청이 전부 "젤리 없음"으로 탈락한다.
    ///
    /// ★ 순서는 계층 구조 순서로 고정된다.
    ///   씬을 수정해 오브젝트를 추가/삭제하면 번호가 밀릴 수 있으니, 그럴 땐 다시 실행하고
    ///   양쪽 모두 같은 씬 파일을 쓰는지 확인할 것.
    /// </summary>
    [MenuItem("Tools/LAN 이식/⑦ 씬 오브젝트 ID 부여", false, 7)]
    public static void AssignSceneIds()
    {
        NetIdentity[] all = Object.FindObjectsByType<NetIdentity>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 계층 경로로 정렬 → 실행할 때마다 같은 순서를 보장
        System.Array.Sort(all, (a, b) =>
            string.CompareOrdinal(FullPath(a.transform), FullPath(b.transform)));

        StringBuilder log = new StringBuilder("=== 씬 오브젝트 ID 부여 ===\n");
        int next = NetConfig.SCENE_ID_BASE;
        int changed = 0;

        foreach (NetIdentity id in all)
        {
            // 프리팹 에셋이 아니라 씬에 실제로 존재하는 것만
            if (id.gameObject.scene.rootCount == 0) continue;

            int want = next++;
            if (id.SceneNetId == want) continue;

            Undo.RecordObject(id, "Assign scene net id");
            id.SceneNetId = want;
            EditorUtility.SetDirty(id);
            changed++;
        }

        log.AppendLine("씬 NetIdentity: " + all.Length + "개");
        log.AppendLine("ID 부여/변경: " + changed + "개");
        log.AppendLine("범위: " + NetConfig.SCENE_ID_BASE + " ~ " + (next - 1));
        log.AppendLine();
        log.AppendLine("★ 씬을 저장하세요 (Ctrl+S). 저장 안 하면 런타임에 0으로 돌아갑니다.");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(log.ToString());
    }

    static string FullPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    /// <summary>이미 채워진 목록을 조건을 다시 적용해 새로 채운다.</summary>
    [MenuItem("Tools/LAN 이식/프리팹 목록 다시 채우기", false, 22)]
    public static void RefillPrefabs()
    {
        GameObject net = Object.FindFirstObjectByType<NetManager>()?.gameObject;
        if (net == null) { Debug.LogError("씬에 NetManager가 없습니다. ④를 먼저 실행하세요."); return; }

        StringBuilder log = new StringBuilder("=== 프리팹 목록 다시 채우기 ===\n");
        forceRefillPrefabs = true;
        try { FillPrefabs(net, log); }
        finally { forceRefillPrefabs = false; }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        log.AppendLine("\n씬을 저장하세요 (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/LAN 이식/④ 현재 씬을 LAN 구성으로", false, 4)]
    public static void Setup()
    {
        if (!EditorUtility.DisplayDialog(
                "씬 LAN 구성",
                "현재 열린 씬을 LAN 구성으로 바꿉니다.\n\n" +
                "· Photon 매니저는 비활성화만 합니다(삭제 X)\n" +
                "· LanNet 오브젝트를 추가합니다\n\n" +
                "저장하지 않으면 되돌릴 수 있습니다.",
                "실행", "취소")) return;

        StringBuilder log = new StringBuilder("=== 씬 LAN 구성 ===\n");

        DisablePhotonManagers(log);
        GameObject net = CreateOrFindLanNet(log);
        FillPrefabs(net, log);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        log.AppendLine();
        log.AppendLine("완료. 씬을 저장하세요 (Ctrl+S).");
        Debug.Log(log.ToString());

        Selection.activeGameObject = net;
    }

    // ─────────────────────────────────────────────
    static void DisablePhotonManagers(StringBuilder log)
    {
        log.AppendLine("[Photon 매니저 비활성화]");
        int n = 0;

        foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string typeName = mb.GetType().Name;

            bool match = false;
            for (int i = 0; i < PhotonManagerTypes.Length; i++)
                if (PhotonManagerTypes[i] == typeName) { match = true; break; }

            if (!match) continue;
            if (!mb.gameObject.activeSelf) continue;

            Undo.RecordObject(mb.gameObject, "Disable Photon manager");
            mb.gameObject.SetActive(false);
            log.AppendLine("  - " + mb.gameObject.name + " (" + typeName + ") 비활성화");
            n++;
        }

        if (n == 0) log.AppendLine("  (없음)");
    }

    static GameObject CreateOrFindLanNet(StringBuilder log)
    {
        log.AppendLine();
        log.AppendLine("[LanNet 오브젝트]");

        NetManager existing = Object.FindFirstObjectByType<NetManager>();
        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
            log.AppendLine("  기존 " + go.name + " 재사용");
        }
        else
        {
            go = new GameObject("LanNet");
            Undo.RegisterCreatedObjectUndo(go, "Create LanNet");
            log.AppendLine("  새로 생성");
        }

        Add<NetManager>(go, log);
        Add<NetWorld>(go, log);
        Add<LanSpawnPoints>(go, log);
        Add<AbsorbMode>(go, log);
        Add<PushMode>(go, log);
        Add<LanGameFlow>(go, log);
        Add<LanBotSpawner>(go, log);
        Add<LanFallOff>(go, log);
        Add<LanDiscovery>(go, log);
        Add<LanLeaderboardUI>(go, log);   // Container/Entry Prefab은 인스펙터에서 연결해야 한다
        Add<NetTestUI>(go, log);
        Add<LanDiagnostics>(go, log);

        return go;
    }

    static void Add<T>(GameObject go, StringBuilder log) where T : Component
    {
        if (go.GetComponent<T>() != null) return;
        go.AddComponent<T>();
        log.AppendLine("  + " + typeof(T).Name);
    }

    /// <summary>
    /// 진짜 플레이어 프리팹을 찾는다.
    /// 기준은 이름이 아니라 <b>기능</b>이다 — NetIdentity(네트워크) + PlayerMovement(조작).
    /// 이름으로 고르면 같은 이름의 껍데기 프리팹을 집는 사고가 난다(실제로 겪음).
    /// </summary>
    static GameObject FindPlayerPrefab(StringBuilder log)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources" });
        List<GameObject> candidates = new List<GameObject>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) continue;
            if (p.GetComponent<NetIdentity>() == null) continue;
            if (p.GetComponentInChildren<PlayerMovement>(true) == null) continue;
            if (p.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) != null) continue;  // 봇 제외

            candidates.Add(p);
        }

        if (candidates.Count == 0) return null;

        if (candidates.Count > 1)
        {
            log.AppendLine("  (후보가 " + candidates.Count + "개 — 첫 번째를 씁니다)");
            foreach (GameObject c in candidates)
                log.AppendLine("      · " + AssetDatabase.GetAssetPath(c));
        }
        return candidates[0];
    }

    /// <summary>NetWorld.prefabs 를 자동으로 채운다. 0번은 반드시 플레이어.</summary>
    static void FillPrefabs(GameObject net, StringBuilder log)
    {
        log.AppendLine();
        log.AppendLine("[프리팹 등록표]");

        NetWorld world = net.GetComponent<NetWorld>();
        if (world == null) return;

        if (world.prefabs != null && world.prefabs.Length > 0 && !forceRefillPrefabs)
        {
            log.AppendLine("  이미 " + world.prefabs.Length + "개 등록됨 — 건드리지 않음");
            log.AppendLine("  (다시 채우려면 [프리팹 목록 다시 채우기] 메뉴)");
            return;
        }

        List<GameObject> list = new List<GameObject>();

        // 0번: 플레이어 — 경로를 고정하지 않고 "실제로 조작 가능한 프리팹"을 찾는다.
        //      (Prefabs/PlayerJellies/NetworkPlayer 는 PlayerMovement가 없는 껍데기였다)
        GameObject player = FindPlayerPrefab(log);
        if (player == null)
        {
            log.AppendLine("  ★ 조작 가능한 플레이어 프리팹을 못 찾았습니다.");
            log.AppendLine("    조건: NetIdentity + PlayerMovement 를 모두 가진 프리팹");
            log.AppendLine("    → [② 프리팹 변환 실행]을 먼저 돌리세요.");
            return;
        }
        list.Add(player);
        log.AppendLine("  [0] " + player.name + "  (플레이어)  " + AssetDatabase.GetAssetPath(player));

        // ═════════════════════════════════════════════
        //  1번부터: 움직이는 젤리
        // ═════════════════════════════════════════════
        //
        // ★ 판별 기준을 NetTransform에서 컴포넌트로 바꿨다.
        //   예전엔 "NetTransform이 있으면 제외"였는데, ⑧ 메뉴가 움직이는 젤리에
        //   NetTransform을 붙이면서 <b>바로 그 젤리들이 전부 빠지게</b> 됐다.
        //   (순서에 따라 결과가 달라지는 조건은 언젠가 반드시 문다)
        //
        //   실제로 구분하려는 건 "젤리냐 / 봇이냐 / 플레이어냐"이므로 그걸 직접 본다.
        List<GameObject> bots = new List<GameObject>();

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { JellyFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null || p == player) continue;
            if (p.GetComponent<NetIdentity>() == null) continue;
            if (p.GetComponentInChildren<PlayerMovement>(true) != null) continue;   // 다른 플레이어 스킨

            // 봇은 따로 모아 뒤에 붙인다
            if (p.GetComponentInChildren<AIDetector>(true) != null) { bots.Add(p); continue; }

            // ★ 런타임에 뿌리는 젤리는 '움직이는 젤리(Wandering/Patrol)'다.
            //   맵에 미리 깔린 NonAI 젤리는 씬 오브젝트라 여기 넣지 않는다
            //   (⑦ 씬 오브젝트 ID 부여로 별도 등록된다).
            if (p.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) == null)
            {
                log.AppendLine("      (제외) " + p.name + " — 맵 배치용 정적 젤리");
                continue;
            }

            list.Add(p);
            log.AppendLine("  [" + (list.Count - 1) + "] " + p.name + "  (움직이는 젤리)");
        }

        // ═════════════════════════════════════════════
        //  마지막: AI 봇
        // ═════════════════════════════════════════════
        //
        // ★ 이게 빠져서 밀치기 씬에 봇이 안 나왔다.
        //   LanBotSpawner는 NetWorld.prefabs에서 봇을 찾는다. 배열에 없으면
        //   "봇 프리팹을 찾지 못했습니다" 경고만 남기고 아무것도 안 뿌린다.
        //   흡수 씬은 손으로 넣어둬서 됐고, 밀치기 씬은 플레이어 하나뿐이었다.
        int jellyCount = list.Count - 1;
        foreach (GameObject b in bots)
        {
            list.Add(b);
            log.AppendLine("  [" + (list.Count - 1) + "] " + b.name + "  (AI 봇)");
        }

        if (bots.Count == 0)
            log.AppendLine("  ★ AI 봇 프리팹을 못 찾았습니다 — [⑨ AI 봇 프리팹 보정]을 먼저 돌리세요.");

        Undo.RecordObject(world, "Fill NetWorld prefabs");
        world.prefabs = list.ToArray();
        EditorUtility.SetDirty(world);

        log.AppendLine("  총 " + list.Count + "개 (젤리 " + jellyCount + "종, 봇 " + bots.Count + "종)");
    }
}
