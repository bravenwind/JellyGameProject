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
        Add<AbsorbMode>(go, log);
        Add<PushMode>(go, log);
        Add<LanGameFlow>(go, log);
        Add<NetTestUI>(go, log);

        return go;
    }

    static void Add<T>(GameObject go, StringBuilder log) where T : Component
    {
        if (go.GetComponent<T>() != null) return;
        go.AddComponent<T>();
        log.AppendLine("  + " + typeof(T).Name);
    }

    /// <summary>NetWorld.prefabs 를 자동으로 채운다. 0번은 반드시 플레이어.</summary>
    static void FillPrefabs(GameObject net, StringBuilder log)
    {
        log.AppendLine();
        log.AppendLine("[프리팹 등록표]");

        NetWorld world = net.GetComponent<NetWorld>();
        if (world == null) return;

        if (world.prefabs != null && world.prefabs.Length > 0)
        {
            log.AppendLine("  이미 " + world.prefabs.Length + "개 등록됨 — 건드리지 않음");
            return;
        }

        List<GameObject> list = new List<GameObject>();

        // 0번: 플레이어
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (player == null || player.GetComponent<NetIdentity>() == null)
        {
            log.AppendLine("  ★ 플레이어 프리팹을 못 찾았거나 NetIdentity가 없습니다: " + PlayerPrefabPath);
            log.AppendLine("    → [② 프리팹 변환 실행]을 먼저 돌리세요.");
            return;
        }
        list.Add(player);
        log.AppendLine("  [0] " + player.name + "  (플레이어)");

        // 1번부터: 젤리 (NetIdentity가 붙었고 NetTransform은 없는 것 = 안 움직이는 것)
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { JellyFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null || p == player) continue;
            if (p.GetComponent<NetIdentity>() == null) continue;
            if (p.GetComponent<NetTransform>() != null) continue;   // 움직이는 것 = 플레이어/봇

            list.Add(p);
            log.AppendLine("  [" + (list.Count - 1) + "] " + p.name);
        }

        Undo.RecordObject(world, "Fill NetWorld prefabs");
        world.prefabs = list.ToArray();
        EditorUtility.SetDirty(world);

        log.AppendLine("  총 " + list.Count + "개 (젤리 " + (list.Count - 1) + "종)");
    }
}
