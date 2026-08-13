using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// GameResult_io 씬에서 Top 3를 좌(1위)→우(3위) 순서로 배치하고,
/// Cinemachine 가상 카메라가 3위→2위→1위→전체 순으로 부드럽게 전환됨.
///
/// 데이터 출처(룸 프로퍼티 / Player 프로퍼티):
///   • Scale / Color_R/G/B  : NetworkPlayerSync.SyncScale + SyncColor
///   • Bot{viewID}_Scale / Color_R/G/B / Name : AIPlayerSync.SyncScale + SyncColor + UpdateBotData
///
/// GameModeManager.SyncAllColorsForResult()가 LoadLevel 직전 호출되어
/// 위 프로퍼티들이 결과 씬으로 넘어옴.
/// </summary>
public class GameResultManager : MonoBehaviour
{
    [Header("프리팹 (비우면 Resources에서 자동 로드)")]
    public GameObject playerJellyPrefab;
    public GameObject botJellyPrefab;
    public string playerPrefabResourcePath = "Prefabs/NetworkPlayer_Bear";
    public string botPrefabResourcePath = "Prefabs/AIPlayer_Bear";

    [Header("배치 설정")]
    [Tooltip("3위가 놓일 우측 끝 기준점 (월드 좌표)")]
    public Vector3 rightAnchor = new Vector3(30f, 0f, 0f);
    [Tooltip("젤리 사이 기본 여유 공간 (월드 단위)")]
    public float padding = 1.0f;
    [Tooltip("scale=1일 때 젤리의 기준 반지름")]
    public float baseRadius = 0.5f;

    [Header("카메라")]
    public Camera resultCamera;
    [Tooltip("CinemachineBrain의 기본 블렌드 시간 (= 카메라 전환 시간)")]
    public float cameraTransitionDuration = 1.2f;
    [Tooltip("포커싱 시 젤리 반지름 × 이 배율만큼 떨어진 거리에서 촬영")]
    public float focusDistanceMultiplier = 3.5f;
    [Tooltip("포커싱 시 카메라 높이 오프셋")]
    public float focusHeightOffset = 0.4f;
    [Tooltip("한 인물에 머무는 시간")]
    public float focusHoldDuration = 2.5f;
    [Tooltip("오버뷰 카메라 거리")]
    public float overviewDistance = 12f;
    [Tooltip("오버뷰 카메라 높이")]
    public float overviewHeight = 4f;
    [Tooltip("가상 카메라 FoV")]
    public float virtualCameraFov = 40f;

    [Tooltip("로딩 커튼이 걷힐 때까지 카메라 시퀀스 시작을 대기하는 최대 시간(신호 유실 대비 안전장치)")]
    public float curtainWaitTimeout = 6f;

    [Header("색상")]
    [Tooltip("저장된 색상이 없을 때 사용할 기본 색")]
    public Color fallbackColor = Color.white;
    [Tooltip("BaseColor_02(밝은 색)를 만들 때 흰색과의 보간 비율")]
    [Range(0f, 1f)] public float baseColor02Lightness = 0.6f;
    public string baseColor01Property = "_BaseColor_01";
    public string baseColor02Property = "_BaseColor_02";
    public string fresnelProperty = "_FresnelColor";

    [Header("UI (선택)")]
    public TextMeshProUGUI rankAnnouncementText;
    public string firstPlaceText = "1위";
    public string secondPlaceText = "2위";
    public string thirdPlaceText = "3위";
    public string finalText = "최종 결과";

    public GameObject buttonRestart;
    public GameObject buttonGameQuit;

    private readonly List<GameObject> _jellies = new List<GameObject>();
    private readonly List<int> _displayOrderRanks = new List<int>();
    private readonly List<CinemachineCamera> _focusCams = new List<CinemachineCamera>();
    private CinemachineCamera _overviewCam;
    private CinemachineBrain _brain;

    private void Start()
    {
        EnsurePrefabs();
        EnsureCameraAndBrain();

        var top = GatherTopEntries();
        if (top.Count == 0)
        {
            Debug.LogWarning("[GameResult] 표시할 엔트리가 없습니다.");
            return;
        }

        SpawnPodium(top);
        BuildVirtualCameras();
        // 포디움/카메라는 커튼 뒤에서 미리 준비해 두되(회색 배경 방지), 카메라 시퀀스는
        // 로딩 커튼이 완전히 걷힌 뒤에 시작한다 → 3위→2위→1위 포커싱을 처음부터 보게 된다.
        StartCoroutine(PlayCameraSequenceWhenCurtainGone());
    }

    /// <summary>
    /// 로딩 커튼(LoadingSceneController)이 걷힐 때까지 기다렸다가 카메라 시퀀스를 시작한다.
    /// 결과 씬은 커튼 뒤에서 미리 로드되므로, 대기 없이 바로 시작하면 3위·2위 포커싱이
    /// 커튼에 가려진 채 흘러가 버린다. 커튼이 없는 진입(직접 로드/테스트)에서는
    /// IsPresenting이 false라 즉시 진행되고, 신호가 유실돼도 curtainWaitTimeout 후 진행된다.
    /// </summary>
    private IEnumerator PlayCameraSequenceWhenCurtainGone()
    {
        float waited = 0f;
        while (LoadingSceneController.IsPresenting && waited < curtainWaitTimeout)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return PlayCameraSequence();
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

    private void EnsureCameraAndBrain()
    {
        if (resultCamera == null) resultCamera = Camera.main;
        if (resultCamera == null)
        {
            var camGO = new GameObject("ResultCamera");
            resultCamera = camGO.AddComponent<Camera>();
            resultCamera.tag = "MainCamera";
        }

        _brain = resultCamera.GetComponent<CinemachineBrain>();
        if (_brain == null) _brain = resultCamera.gameObject.AddComponent<CinemachineBrain>();

        _brain.DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Styles.EaseInOut, cameraTransitionDuration);
    }

    // ──────────────────────────────────────────────
    // 데이터 수집
    // ──────────────────────────────────────────────

    // 최종 순위는 게임 씬에서 호스트가 방송해 준 것을 그대로 쓴다.
    // 결과 씬에는 플레이어·봇 오브젝트가 없어서(씬을 넘기며 파괴됨) 여기서 다시 셀 수 없다.
    private List<JellyNet.LanScoreboard.Entry> GatherTopEntries()
    {
        var final = JellyNet.LanScoreboard.FinalStandings;

        if (final == null || final.Count == 0)
        {
            Debug.LogWarning("[GameResult] 최종 순위가 비어 있습니다 — 게임 씬에서 방송이 오지 않았습니다.");
            return new List<JellyNet.LanScoreboard.Entry>();
        }

        var top = new List<JellyNet.LanScoreboard.Entry>();

        for (int i = 0; i < final.Count && i < 3; i++)
        {
            var e = final[i];
            if (e.color.a <= 0.01f)
                e.color = fallbackColor;
            top.Add(e);
        }

        return top;
    }

    // ──────────────────────────────────────────────
    // 배치
    // ──────────────────────────────────────────────

    private void SpawnPodium(List<JellyNet.LanScoreboard.Entry> top)
    {
        int count = top.Count;
        float[] radii = new float[count];
        for (int i = 0; i < count; i++)
            radii[i] = Mathf.Max(0.1f, baseRadius * top[i].scale);

        Vector3[] positions = new Vector3[count];
        int rightmost = count - 1;

        // rightAnchor는 "3위가 배치될 시작 위치(월드 좌표)"
        positions[rightmost] = rightAnchor;

        for (int i = rightmost - 1; i >= 0; i--)
        {
            float gap = radii[i + 1] + padding + radii[i];
            positions[i] = positions[i + 1] + Vector3.left * gap;
        }

        // 중앙 캐릭터(인덱스 count/2)를 X=0 기준으로 정렬
        // avgX 방식은 각 캐릭터 크기 차이로 인해 중앙 캐릭터가 화면 중심에서 벗어남
        int centerIdx = count / 2;
        float centerX = positions[centerIdx].x;
        for (int i = 0; i < count; i++)
            positions[i] -= new Vector3(centerX, 0f, 0f);

        for (int i = 0; i < count; i++)
        {
            var entry = top[i];
            GameObject prefab = entry.isBot && botJellyPrefab != null ? botJellyPrefab : playerJellyPrefab;
            if (prefab == null) continue;

            // 여기서 pos는 X, Z 위치만 중요해지고, Y는 바닥 보정에 쓰임
            GameObject go = InstantiateDisplayOnly(prefab, positions[i], Quaternion.identity);
            go.transform.Rotate(new Vector3(0, 180, 0)); // 카메라 정면을 보도록 180도 회전

            // 1. 크기 적용 (Scale을 먼저 키워야 bounds가 제대로 갱신됨)
            go.transform.localScale = Vector3.one * entry.scale;
            go.SetActive(true);
            GroundToFloor(go, positions[i].y);
            SetupNameTag(go, entry.name);

            ApplyJellyColor(go, entry.color);

            _jellies.Add(go);
            _displayOrderRanks.Add(i + 1);
        }
    }

    private GameObject InstantiateDisplayOnly(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        bool wasActive = prefab.activeSelf;
        prefab.SetActive(false);
        GameObject go = Instantiate(prefab, pos, rot);
        prefab.SetActive(wasActive);

        // 흡수 모드에서는 방망이(배트)를 숨긴다(밀치기 모드는 그대로 표시).
        // 반드시 인스턴스(go)에 적용한다 — 공유 프리팹을 건드리면 변경이 Resources
        // 캐시에 남아 다음 매치까지 누수된다. PlayerMovement/AIPlayerMovement는
        // 바로 아래 Strip에서 제거되므로, 그 전에 batPivot을 끈다.
        if (GameState.CurrentGameMode == GameModeType.Absorb)
            HideBat(go);

        // 비활성 상태 → 모든 Awake 미실행. 문제 컴포넌트를 즉시 제거.
        StripNetworkingAndGameplay(go);
        return go;
    }

    // 결과 젤리 인스턴스의 방망이(배트)를 숨긴다. PlayerMovement/AIPlayerMovement의
    // batPivot은 인게임 로직(AIPlayerMovement.UpdateBatVisibility 등)도 null 가드를
    // 두므로 동일하게 미할당을 안전 처리한다.
    private static void HideBat(GameObject go)
    {
        var pm = go.GetComponent<PlayerMovement>();
        if (pm != null && pm.batPivot != null)
            pm.batPivot.gameObject.SetActive(false);

        var ai = go.GetComponent<AIPlayerMovement>();
        if (ai != null && ai.batPivot != null)
            ai.batPivot.gameObject.SetActive(false);
    }

    private void StripNetworkingAndGameplay(GameObject root)
    {
        DestroyAll<AIPlayerMovement>(root);
        DestroyAll<WanderingAI>(root);
        DestroyAll<AIWaypointPatrol>(root);
        DestroyAll<PlayerMovement>(root);
        DestroyAll<PlayerAbsorber>(root);
        DestroyAll<PlayerAbsorbingManager>(root);
        DestroyAll<PlayerBridge>(root);
        DestroyAll<BotBridge>(root);
        DestroyAll<UnityEngine.AI.NavMeshAgent>(root);

        // PlayerColorVisual.Start가 색을 흰색으로 덮어쓰지 않도록 미리 제거 → 우리가 직접 색 적용
        DestroyAll<PlayerColorVisual>(root);

        // Cloth를 DestroyImmediate하면 SkinnedMeshRenderer 데이터가 오염되므로 비활성화만
        foreach (var sb in root.GetComponentsInChildren<SoftBody3D>(true))
            sb.enabled = false;
        foreach (var cloth in root.GetComponentsInChildren<Cloth>(true))
            cloth.enabled = false;

        // photonView를 참조하는 모든 Photon View 컴포넌트를 PhotonView보다 먼저 제거
        DestroyAll<PhotonTransformView>(root);
        DestroyAll<PhotonAnimatorView>(root);
        DestroyAll<PhotonRigidbodyView>(root);
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

    private static Renderer FindJellyRenderer(GameObject root)
    {
        var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null) return smr;
        return root.GetComponentInChildren<Renderer>(true);
    }

    private static void GroundToFloor(GameObject go, float floorY)
    {
        Transform bottom = go.transform.Find("BottomTransform");
        if (bottom != null)
        {
            float offset = floorY - bottom.position.y;
            go.transform.position += new Vector3(0f, offset, 0f);
            return;
        }
    }

    private void SetupNameTag(GameObject root, string playerName)
    {
        var tag = root.GetComponentInChildren<NameTagBillboard>(true);
        if (tag == null) return;
        tag.gameObject.SetActive(true);
        tag.SetName(playerName);

        Transform top = root.transform.Find("TopTransform");
        if (top != null) tag.topTransform = top;
    }

    private void ApplyJellyColor(GameObject root, Color color)
    {
        var rend = FindJellyRenderer(root);
        if (rend == null) return;

        // material은 매번 instance가 생성되도록 .material 접근 (sharedMaterial이 아님)
        var mat = rend.material;
        Color lighter = Color.Lerp(color, Color.white, baseColor02Lightness);
        if (mat.HasProperty(baseColor01Property)) mat.SetColor(baseColor01Property, color);
        if (mat.HasProperty(baseColor02Property)) mat.SetColor(baseColor02Property, lighter);
        if (mat.HasProperty(fresnelProperty))     mat.SetColor(fresnelProperty, color);
    }

    // ──────────────────────────────────────────────
    // Cinemachine 가상 카메라 생성
    // ──────────────────────────────────────────────

    private void BuildVirtualCameras()
    {
        // 각 젤리마다 포커싱용 가상 카메라
        for (int i = 0; i < _jellies.Count; i++)
        {
            var jelly = _jellies[i];
            float radius = Mathf.Max(0.2f, jelly.transform.localScale.x * baseRadius);
            float distance = ComputeFocusDistance(radius);

            Vector3 centerWorld = jelly.transform.position + Vector3.up * radius;
            Vector3 camPos = centerWorld + new Vector3(0f, focusHeightOffset, -distance);
            Quaternion camRot = Quaternion.LookRotation(centerWorld - camPos);

            var go = new GameObject($"VCam_Focus_Rank{_displayOrderRanks[i]}");
            go.transform.SetParent(transform, false);
            go.transform.position = camPos;
            go.transform.rotation = camRot;

            var vcam = go.AddComponent<CinemachineCamera>();
            ApplyLens(vcam, virtualCameraFov);
            vcam.Priority = 0;

            _focusCams.Add(vcam);
        }

        // 오버뷰 카메라
        Vector3 center = Vector3.zero;
        foreach (var j in _jellies) center += j.transform.position;
        if (_jellies.Count > 0) center /= _jellies.Count;

        Vector3 ovPos = center + new Vector3(0f, overviewHeight, -overviewDistance);
        Quaternion ovRot = Quaternion.LookRotation(center - ovPos);
        var ovGo = new GameObject("VCam_Overview");
        ovGo.transform.SetParent(transform, false);
        ovGo.transform.position = ovPos;
        ovGo.transform.rotation = ovRot;
        _overviewCam = ovGo.AddComponent<CinemachineCamera>();
        ApplyLens(_overviewCam, virtualCameraFov);
        _overviewCam.Priority = 0;
    }

    private float ComputeFocusDistance(float radius)
    {
        // 전신이 들어오도록: half-FoV 기반 최소 거리 + 약간의 여유
        float fov = virtualCameraFov;
        if (resultCamera.orthographic)
        {
            resultCamera.orthographicSize = Mathf.Max(resultCamera.orthographicSize, radius * 1.3f);
            return radius * focusDistanceMultiplier;
        }
        float halfFovRad = fov * 0.5f * Mathf.Deg2Rad;
        float required = radius / Mathf.Tan(halfFovRad) + radius;
        return Mathf.Max(radius * focusDistanceMultiplier, required);
    }

    private static void ApplyLens(CinemachineCamera vcam, float fov)
    {
        // Lens는 struct이므로 복사 → 수정 → 다시 대입
        var lens = vcam.Lens;
        lens.FieldOfView = fov;
        vcam.Lens = lens;
    }

    // ──────────────────────────────────────────────
    // 시퀀스 (Cinemachine Priority 전환)
    // ──────────────────────────────────────────────

    private IEnumerator PlayCameraSequence()
    {
        if (_jellies.Count == 0) yield break;
        EnsureRankText();

        // 우측(낮은 순위) → 좌측(높은 순위): _jellies[last] = 3위, _jellies[0] = 1위
        for (int i = _jellies.Count - 1; i >= 0; i--)
        {
            // 이전 활성 카메라들을 0으로 낮추고 현재만 활성화
            DeactivateAllVcams();
            _focusCams[i].Priority = 100;

            SetRankText(GetRankString(_displayOrderRanks[i]));

            if (!_brain.IsBlending)
            {
                yield return new WaitForSeconds(0.5f);
                Animator playerAnimator = _jellies[i].GetComponentInChildren<Animator>();
                if (playerAnimator != null)
                {
                    playerAnimator.SetTrigger("Jump");
                }
            }

            // 블렌드 시간 + 머무는 시간
            yield return new WaitForSeconds(cameraTransitionDuration + focusHoldDuration);
        }

        // 마지막: 오버뷰
        DeactivateAllVcams();
        _overviewCam.Priority = 100;
        SetRankText(finalText);

        // 블렌딩이 완료될 때까지 대기한 뒤 버튼을 표시한다.
        // 이전 코드에서는 블렌딩 중일 때 yield return null 1회 후 코루틴이 종료되어
        // 버튼이 영원히 나타나지 않았다.
        while (_brain.IsBlending)
            yield return null;

        if (buttonRestart != null) buttonRestart.SetActive(true);
        if (buttonGameQuit != null) buttonGameQuit.SetActive(true);
    }

    private void DeactivateAllVcams()
    {
        foreach (var cam in _focusCams)
            if (cam != null) cam.Priority = 0;
        if (_overviewCam != null) _overviewCam.Priority = 0;
    }

    private string GetRankString(int rank)
    {
        if (GameState.CurrentGameMode == GameModeType.Push)
        {
            firstPlaceText = "우승!";
        }

        switch (rank)
        {
            case 1: return firstPlaceText;
            case 2: return secondPlaceText;
            case 3: return thirdPlaceText;
            default: return $"{rank}위";
        }
    }

    // ──────────────────────────────────────────────
    // UI 헬퍼
    // ──────────────────────────────────────────────

    private void EnsureRankText()
    {
        if (rankAnnouncementText != null) return;

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
