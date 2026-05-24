using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;

/// <summary>
/// GameResult_io 씬에서 Top 3를 좌측(1위) → 중앙(2위) → 우측(3위) 순으로 배치하고
/// 카메라가 3위 → 2위 → 1위 → 전체 순으로 포커싱하는 연출 컨트롤러.
///
/// 데이터 출처: PhotonNetwork.PlayerList(스케일 = CustomProperties["Scale"]) +
///              Room CustomProperties("Bot{viewID}_Name", "Bot{viewID}_Scale").
///
/// 프리팹은 인스펙터에 직접 할당해도 되고, 비워두면 Resources에서 자동 로드.
/// </summary>
public class GameResultDirector : MonoBehaviour
{
    [Header("프리팹 (비우면 Resources에서 자동 로드)")]
    public GameObject playerJellyPrefab;
    public GameObject botJellyPrefab;
    public string playerPrefabResourcePath = "Prefabs/NetworkPlayer_Bear";
    public string botPrefabResourcePath = "Prefabs/AIPlayer_Bear";

    [Header("배치 설정")]
    [Tooltip("3위가 놓일 우측 끝 기준점 (월드 좌표)")]
    public Vector3 rightAnchor = new Vector3(3f, 0f, 0f);
    [Tooltip("젤리 사이 기본 여유 공간 (월드 단위)")]
    public float padding = 1.0f;
    [Tooltip("scale=1일 때 젤리의 기준 반지름")]
    public float baseRadius = 0.5f;

    [Header("카메라")]
    public Camera resultCamera;
    [Tooltip("포커싱 시 젤리 반지름 × 이 배율만큼 떨어진 거리에서 촬영")]
    public float focusDistanceMultiplier = 3.5f;
    [Tooltip("포커싱 시 카메라 높이 오프셋")]
    public float focusHeightOffset = 0.4f;
    [Tooltip("한 인물에 머무는 시간")]
    public float focusHoldDuration = 2.5f;
    [Tooltip("카메라가 다음 위치로 이동하는 데 걸리는 시간")]
    public float cameraTransitionDuration = 1.2f;
    [Tooltip("오버뷰 카메라 거리")]
    public float overviewDistance = 12f;
    [Tooltip("오버뷰 카메라 높이")]
    public float overviewHeight = 4f;

    [Header("UI (선택)")]
    [Tooltip("순위 안내 텍스트 (비워두면 자동 생성)")]
    public TextMeshProUGUI rankAnnouncementText;
    public string firstPlaceText = "1위";
    public string secondPlaceText = "2위";
    public string thirdPlaceText = "3위";
    public string finalText = "FINAL RESULT";

    private readonly List<GameObject> _jellies = new List<GameObject>();
    private readonly List<int> _displayOrderRanks = new List<int>(); // _jellies[i]가 몇 위인지

    private void Start()
    {
        EnsurePrefabs();
        EnsureCamera();

        var top = GatherTopEntries();
        if (top.Count == 0)
        {
            Debug.LogWarning("[GameResult] 표시할 엔트리가 없습니다.");
            return;
        }

        SpawnPodium(top);
        StartCoroutine(PlayCameraSequence());
    }

    // ──────────────────────────────────────────────
    // 셋업
    // ──────────────────────────────────────────────

    private void EnsurePrefabs()
    {
        if (playerJellyPrefab == null)
            playerJellyPrefab = Resources.Load<GameObject>(playerPrefabResourcePath);
        if (botJellyPrefab == null)
            botJellyPrefab = Resources.Load<GameObject>(botPrefabResourcePath);

        if (playerJellyPrefab == null)
            Debug.LogError($"[GameResult] 플레이어 프리팹 로드 실패: Resources/{playerPrefabResourcePath}");
        if (botJellyPrefab == null)
            Debug.LogWarning($"[GameResult] 봇 프리팹 로드 실패 → 플레이어 프리팹 재사용");
    }

    private void EnsureCamera()
    {
        if (resultCamera == null) resultCamera = Camera.main;
        if (resultCamera == null)
        {
            var camGO = new GameObject("ResultCamera");
            resultCamera = camGO.AddComponent<Camera>();
            resultCamera.tag = "MainCamera";
        }
    }

    // ──────────────────────────────────────────────
    // 데이터 수집
    // ──────────────────────────────────────────────

    private struct Entry
    {
        public string name;
        public float scale;
        public bool isBot;
    }

    private List<Entry> GatherTopEntries()
    {
        var dm = DataManager.Instance;
        float defaultScale = dm != null ? dm.startingScale : 2f;
        var entries = new List<Entry>();

        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            Debug.LogWarning("[GameResult] 룸 정보 없음 — 더미 데이터로 동작");
            return entries;
        }

        foreach (Player p in PhotonNetwork.PlayerList)
        {
            float scale = (p.CustomProperties != null &&
                           p.CustomProperties.TryGetValue("Scale", out object sc))
                ? (float)sc : defaultScale;
            entries.Add(new Entry { name = p.NickName, scale = scale, isBot = false });
        }

        var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
        foreach (var key in roomProps.Keys)
        {
            string keyStr = key.ToString();
            if (!keyStr.EndsWith("_Name")) continue;

            object nameVal = roomProps[keyStr];
            if (nameVal == null) continue;

            string prefix = keyStr.Replace("_Name", "");
            float scale = (roomProps.TryGetValue($"{prefix}_Scale", out object sv) && sv != null)
                ? (float)sv : defaultScale;
            entries.Add(new Entry { name = nameVal.ToString(), scale = scale, isBot = true });
        }

        return entries.OrderByDescending(e => e.scale).Take(3).ToList();
    }

    // ──────────────────────────────────────────────
    // 배치
    // ──────────────────────────────────────────────

    private void SpawnPodium(List<Entry> top)
    {
        // 사용자 요청: 우측부터 순서대로 3위 - 2위 - 1위
        // 인덱스 매핑 (top[0]=1위, top[1]=2위, top[2]=3위)
        // 시각적 배치 (좌측부터): 1위 - 2위 - 3위
        // 우측 anchor에서 시작해 좌측으로 누적 배치 (사이즈 + 패딩 누적)

        int count = top.Count;
        float[] radii = new float[count];
        for (int i = 0; i < count; i++)
            radii[i] = Mathf.Max(0.1f, baseRadius * top[i].scale);

        // positions[i]: top[i] (i=0→1위, i=1→2위, i=2→3위)의 월드 좌표
        Vector3[] positions = new Vector3[count];

        // 마지막 인덱스 (3위)를 우측 anchor에 배치
        int rightmost = count - 1;
        positions[rightmost] = rightAnchor;

        // 우측에서 좌측으로 (i = rightmost-1 ... 0)
        for (int i = rightmost - 1; i >= 0; i--)
        {
            float gap = radii[i + 1] + padding + radii[i];
            positions[i] = positions[i + 1] + Vector3.left * gap;
        }

        // 가운데 정렬(centroid를 X=0으로): 모든 X 평균을 0으로 맞춤
        float avgX = 0f;
        for (int i = 0; i < count; i++) avgX += positions[i].x;
        avgX /= count;
        for (int i = 0; i < count; i++)
            positions[i] -= new Vector3(avgX, 0f, 0f);

        // 인스턴스화 (네트워킹 컴포넌트 Awake가 실행되지 않도록 비활성 상태로 인스턴스화)
        for (int i = 0; i < count; i++)
        {
            var entry = top[i];
            GameObject prefab = entry.isBot && botJellyPrefab != null ? botJellyPrefab : playerJellyPrefab;
            if (prefab == null) continue;

            GameObject go = InstantiateDisplayOnly(prefab, positions[i], Quaternion.identity);
            go.transform.localScale = Vector3.one * entry.scale;
            SetupNameTag(go, entry.name);
            go.SetActive(true);

            _jellies.Add(go);
            _displayOrderRanks.Add(i + 1); // 1-indexed rank
        }
    }

    /// <summary>
    /// 네트워킹/게임플레이 컴포넌트의 Awake가 실행되지 않도록
    /// 프리팹을 잠시 비활성화한 채 인스턴스화 → 문제 컴포넌트 제거 → 호출자가 활성화.
    /// </summary>
    private GameObject InstantiateDisplayOnly(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        bool wasActive = prefab.activeSelf;
        prefab.SetActive(false);
        GameObject go = Instantiate(prefab, pos, rot);
        prefab.SetActive(wasActive);

        // 이 시점에 go는 비활성 상태 → Awake 미실행. 문제 컴포넌트를 즉시 제거.
        StripNetworkingAndGameplay(go);
        return go;
    }

    private void StripNetworkingAndGameplay(GameObject root)
    {
        DestroyAll<NetworkPlayerSync>(root);
        DestroyAll<AIPlayerMovement>(root);
        DestroyAll<AIPlayerSync>(root);
        DestroyAll<WanderingAI>(root);
        DestroyAll<AIWaypointPatrol>(root);
        DestroyAll<PlayerMovement>(root);
        DestroyAll<PlayerAbsorber>(root);
        DestroyAll<PlayerAbsorbingManager>(root);
        DestroyAll<PlayerBridge>(root);
        DestroyAll<BotBridge>(root);
        DestroyAll<UnityEngine.AI.NavMeshAgent>(root);
        // PhotonView는 다른 컴포넌트가 참조했을 수 있으므로 마지막에
        DestroyAll<PhotonView>(root);

        foreach (var col in root.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private static void DestroyAll<T>(GameObject root) where T : Component
    {
        foreach (var c in root.GetComponentsInChildren<T>(true))
            DestroyImmediate(c);
    }

    private void SetupNameTag(GameObject root, string playerName)
    {
        var tag = root.GetComponentInChildren<NameTagBillboard>(true);
        if (tag == null) return;
        tag.gameObject.SetActive(true);
        tag.SetName(playerName);
    }

    // ──────────────────────────────────────────────
    // 카메라 시퀀스
    // ──────────────────────────────────────────────

    private IEnumerator PlayCameraSequence()
    {
        if (_jellies.Count == 0 || resultCamera == null) yield break;

        EnsureRankText();

        // 포커싱 순서: 우측(낮은 순위) → 좌측(높은 순위) = 3위 → 2위 → 1위
        // _jellies 인덱스: 0=1위(좌측), 1=2위, 2=3위(우측)
        int last = _jellies.Count - 1;
        for (int i = last; i >= 0; i--)
        {
            int rank = _displayOrderRanks[i];
            SetRankText(GetRankString(rank));
            yield return StartCoroutine(LerpCameraToFocus(_jellies[i]));
            yield return new WaitForSeconds(focusHoldDuration);
        }

        // 마지막: 전체 보기
        SetRankText(finalText);
        yield return StartCoroutine(LerpCameraToOverview());
    }

    private string GetRankString(int rank)
    {
        switch (rank)
        {
            case 1: return firstPlaceText;
            case 2: return secondPlaceText;
            case 3: return thirdPlaceText;
            default: return $"{rank}위";
        }
    }

    private IEnumerator LerpCameraToFocus(GameObject target)
    {
        // 젤리 반지름 = 현재 localScale × baseRadius
        float radius = Mathf.Max(0.2f, target.transform.localScale.x * baseRadius);

        // 젤리 전신이 보이도록 거리/높이 계산
        // 수직 FoV 기반: distance >= radius / tan(fov/2) 보장
        float distance = radius * focusDistanceMultiplier;
        if (resultCamera.orthographic)
        {
            resultCamera.orthographicSize = Mathf.Max(resultCamera.orthographicSize, radius * 1.3f);
        }
        else
        {
            float halfFovRad = resultCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float requiredDistance = radius / Mathf.Tan(halfFovRad) + radius;
            distance = Mathf.Max(distance, requiredDistance);
        }

        Vector3 centerWorld = target.transform.position + Vector3.up * radius;
        Vector3 targetPos = centerWorld + new Vector3(0f, focusHeightOffset, -distance);
        Quaternion targetRot = Quaternion.LookRotation(centerWorld - targetPos);

        yield return LerpCamera(targetPos, targetRot, cameraTransitionDuration);
    }

    private IEnumerator LerpCameraToOverview()
    {
        Vector3 center = Vector3.zero;
        float maxRadius = 0f;
        foreach (var j in _jellies)
        {
            center += j.transform.position;
            maxRadius = Mathf.Max(maxRadius, j.transform.localScale.x * baseRadius);
        }
        center /= _jellies.Count;

        Vector3 targetPos = center + new Vector3(0f, overviewHeight, -overviewDistance);
        Quaternion targetRot = Quaternion.LookRotation(center - targetPos);

        yield return LerpCamera(targetPos, targetRot, cameraTransitionDuration);
    }

    private IEnumerator LerpCamera(Vector3 targetPos, Quaternion targetRot, float duration)
    {
        Vector3 startPos = resultCamera.transform.position;
        Quaternion startRot = resultCamera.transform.rotation;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            resultCamera.transform.position = Vector3.Lerp(startPos, targetPos, p);
            resultCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, p);
            yield return null;
        }

        resultCamera.transform.position = targetPos;
        resultCamera.transform.rotation = targetRot;
    }

    // ──────────────────────────────────────────────
    // UI 헬퍼
    // ──────────────────────────────────────────────

    private void EnsureRankText()
    {
        if (rankAnnouncementText != null) return;

        // 자동 생성
        var canvasGO = new GameObject("ResultRankCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var textGO = new GameObject("RankText");
        textGO.transform.SetParent(canvasGO.transform, false);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 80;
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;

        var rt = text.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.85f);
        rt.anchorMax = new Vector2(0.5f, 0.85f);
        rt.sizeDelta = new Vector2(800, 120);
        rt.anchoredPosition = Vector2.zero;

        rankAnnouncementText = text;
    }

    private void SetRankText(string s)
    {
        if (rankAnnouncementText != null) rankAnnouncementText.text = s;
    }
}
