using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using JellyNet;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// GameResult_io 씬에서 Top 3를 좌(1위)→우(3위) 순서로 배치하고,
/// Cinemachine 가상 카메라가 3위→2위→1위→전체 순으로 부드럽게 전환됨.
///
/// 데이터 출처는 LanScoreboard다. 판이 끝날 때 호스트가 최종 순위를 방송하고,
/// 그 값이 씬을 넘어 여기로 전달된다.
/// </summary>
public class GameResultManager : MonoBehaviour
{
    [Header("프리팹")]
    [SerializeField] private GameObject playerJellyPrefab;
    [SerializeField] private GameObject botJellyPrefab;

    [Header("배치 설정")]
    // ★ 예전엔 rightAnchor(= 3위가 놓일 우측 끝)였다
    //   그런데 이 값의 x는 <b>아무 영향이 없었다.</b> 오른쪽부터 왼쪽으로 자리를 잡은 뒤
    //   맨 마지막에 "가운데 인물을 x=0으로" 다시 밀어서 x를 통째로 지웠기 때문이다.
    //   실제로 흡수 씬엔 x=30, 밀치기 씬엔 x=0이 들어 있었는데 결과는 같았다 —
    //   이름이 거짓말을 해서 누군가 30을 넣어본 흔적이다.
    //   살아남던 건 y(바닥 높이)와 z(깊이)뿐이었다.
    //
    //   지금은 처음부터 가운데를 기준으로 잡는다. 두 단계가 한 단계가 되고,
    //   인스펙터의 x가 실제로 화면 중심을 옮긴다.
    [Tooltip("가운데 인물(2위)이 놓일 자리. 1위·3위가 좌우로 같은 간격만큼 벌어진다. y는 바닥 높이.")]
    [SerializeField] private Vector3 podiumCenter = new Vector3(0f, 0f, 0f);
    [Tooltip("젤리 사이 기본 여유 공간 (월드 단위)")]
    [SerializeField] private float padding = 1.0f;

    [Header("카메라")]
    [SerializeField] private Camera resultCamera;
    [Tooltip("CinemachineBrain의 기본 블렌드 시간 (= 카메라 전환 시간)")]
    [SerializeField] private float cameraTransitionDuration = 1.2f;
    [Tooltip("포커싱 시 젤리 반지름 × 이 배율만큼 떨어진 거리에서 촬영")]
    [SerializeField] private float focusDistanceMultiplier = 3.5f;
    [Tooltip("포커싱 시 카메라 높이 오프셋")]
    [SerializeField] private float focusHeightOffset = 0.4f;
    [Tooltip("한 인물에 머무는 시간")]
    [SerializeField] private float focusHoldDuration = 2.5f;
    [Tooltip("오버뷰 카메라 거리")]
    [SerializeField] private float overviewDistance = 12f;
    [Tooltip("오버뷰 카메라 높이")]
    [SerializeField] private float overviewHeight = 4f;
    [Tooltip("가상 카메라 FoV")]
    [SerializeField] private float virtualCameraFov = 40f;

    [Tooltip("로딩 커튼이 걷힐 때까지 카메라 시퀀스 시작을 대기하는 최대 시간(신호 유실 대비 안전장치)")]
    [SerializeField] private float curtainWaitTimeout = 6f;

    [Header("색상")]
    [Tooltip("BaseColor_02(밝은 색)를 만들 때 흰색과의 보간 비율")]
    [Range(0f, 1f)] [SerializeField] private float baseColor02Lightness = 0.6f;
    [SerializeField] private string baseColor01Property = "_BaseColor_01";
    [SerializeField] private string baseColor02Property = "_BaseColor_02";
    [SerializeField] private string fresnelProperty = "_FresnelColor";

    [Header("UI (선택)")]
    [SerializeField] private TextMeshProUGUI rankAnnouncementText;
    [SerializeField] private string firstPlaceText = "1위";
    [SerializeField] private string secondPlaceText = "2위";
    [SerializeField] private string thirdPlaceText = "3위";
    [SerializeField] private string finalText = "최종 결과";

    [SerializeField] private GameObject buttonRestart;
    [SerializeField] private GameObject buttonGameQuit;

    private readonly List<GameObject> jellies = new List<GameObject>();
    private readonly List<int> displayOrderRanks = new List<int>();
    private readonly List<CinemachineCamera> focusCams = new List<CinemachineCamera>();
    private CinemachineCamera overviewCam;
    private CinemachineBrain brain;

    private void Start()
    {
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

    // ★ 카메라와 Brain을 런타임에 만들던 폴백을 걷어냈다
    //   resultCamera가 비면 Camera.main → 그것도 없으면 new GameObject("ResultCamera")까지
    //   내려갔는데, 두 결과 씬 모두 인스펙터에 Camera가 꽂혀 있어 한 번도 내려간 적이 없다.
    //   씬에 있어야 할 것을 코드가 몰래 만들어주면, 씬을 보는 사람은 왜 카메라가 없는데
    //   화면이 나오는지 알 수 없다. 없으면 없다고 말하고 멈춘다.
    //
    //   DefaultBlend만 코드에 남긴다. 이 값은 아래 PlayCameraSequence가 대기 시간으로도
    //   쓰기 때문에, 인스펙터로 옮기면 '블렌드 시간'의 출처가 둘이 된다.
    private void EnsureCameraAndBrain()
    {
        if (resultCamera == null)
        {
            Debug.LogError("[GameResult] resultCamera가 비어 있습니다. 씬의 Camera를 인스펙터에 연결하세요.");
            return;
        }

        brain = resultCamera.GetComponent<CinemachineBrain>();
        if (brain == null)
        {
            Debug.LogError("[GameResult] " + resultCamera.name + " 에 CinemachineBrain이 없습니다. 씬에서 추가하세요.");
            return;
        }

        brain.DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Styles.EaseInOut, cameraTransitionDuration);
    }

    // ──────────────────────────────────────────────
    // 데이터 수집
    // ──────────────────────────────────────────────

    // 최종 순위는 게임 씬에서 호스트가 방송해 준 것을 그대로 쓴다.
    // 결과 씬에는 플레이어·봇 오브젝트가 없어서(씬을 넘기며 파괴됨) 여기서 다시 셀 수 없다.
    private List<LanScoreboard.Entry> GatherTopEntries()
    {
        var final = LanScoreboard.FinalStandings;

        if (final == null || final.Count == 0)
        {
            Debug.LogWarning("[GameResult] 최종 순위가 비어 있습니다 — 게임 씬에서 방송이 오지 않았습니다.");
            return new List<LanScoreboard.Entry>();
        }

        //FinalStandings에는 참가자 전원이 순위대로 들어 있다. 포디움은 셋뿐이라 앞에서 잘라 온다
        //— 이 루프가 하던 일이 그거였다(나머지 절반은 색 폴백이었는데 그건 지웠다).
        return final.GetRange(0, Mathf.Min(3, final.Count));
    }

    // ──────────────────────────────────────────────
    // 배치
    // ──────────────────────────────────────────────

    private void SpawnPodium(List<LanScoreboard.Entry> top)
    {
        int count = top.Count;
        float[] radii = new float[count];
        for (int i = 0; i < count; i++)
            radii[i] = Mathf.Max(0.1f, JellyRadius(top[i].scale));

        //가운데 인물을 podiumCenter에 놓고 좌우로 벌린다. 이웃한 둘 사이의 간격은
        //'두 몸의 반지름 + padding'이라 크기가 달라도 옷깃이 겹치지 않는다
        int centerIdx = count / 2;
        Vector3[] positions = new Vector3[count];
        positions[centerIdx] = podiumCenter;

        for (int i = centerIdx - 1; i >= 0; i--)
            positions[i] = positions[i + 1] + Vector3.left * (radii[i + 1] + padding + radii[i]);

        for (int i = centerIdx + 1; i < count; i++)
            positions[i] = positions[i - 1] + Vector3.right * (radii[i - 1] + padding + radii[i]);

        for (int i = 0; i < count; i++)
        {
            var entry = top[i];
            GameObject prefab = entry.isBot && botJellyPrefab != null ? botJellyPrefab : playerJellyPrefab;
            if (prefab == null)
                continue;

            // pos는 X·Z 자리이고, Y는 바닥 높이로 쓰인다
            GameObject go = Instantiate(prefab, positions[i], Quaternion.identity);
            go.transform.Rotate(new Vector3(0, 180, 0)); // 카메라 정면을 보도록 180도 회전

            //바닥 정렬보다 먼저 키운다 — BottomTransform의 월드 높이가 크기에 따라 달라진다
            go.transform.localScale = Vector3.one * entry.scale;

            ApplyBatVisibility(go);
            GroundToFloor(go, positions[i].y);
            SetupNameTag(go, entry.name);
            ApplyJellyColor(go, entry.color);

            jellies.Add(go);
            displayOrderRanks.Add(i + 1);
        }
    }

    // ★ '인게임 프리팹을 런타임에 분해하기'를 걷어냈다
    //   예전엔 NetworkPlayer_Bear / AIPlayer_Bear를 그대로 가져와, <b>비활성 상태로
    //   Instantiate해 Awake를 막고</b> 그 틈에 컴포넌트 일곱 종을 DestroyImmediate로
    //   뜯어낸 뒤 켰다. 콜라이더·Rigidbody·Cloth는 하나씩 꺼서 재웠다.
    //
    //   문제는 그 목록이 <b>인게임 프리팹을 따라다녀야 했다</b>는 것이다. 프리팹에
    //   컴포넌트가 하나 늘면 여기도 한 줄 늘려야 하는데, 늘리는 걸 잊으면 아무 신호
    //   없이 결과 화면까지 따라온다. 실제로 LevelUpFloaterPool이 목록에 없어서 결과
    //   젤리마다 성장 팝업 풀이 딸려 왔다. 그리고 씬을 보는 사람은 프리팹에 무엇이
    //   붙어 있는지로는 결과 화면에 무엇이 도는지 알 수 없었다.
    //
    //   지금은 NetworkPlayer_Bear_Result / AIPlayer_Bear_Result 가 결과 화면에 필요한
    //   것만 들고 있다. 코드는 낳아서 놓고, 모드에 따라 달라지는 것 하나(배트)만 정한다.

    private const string BatPivotName = "BatPivot";

    /// <summary>
    /// 배트는 밀치기 모드에서만 보인다. <b>프리팹의 초기 상태와 무관하게</b> 여기서 정한다 —
    /// 두 결과 프리팹의 BatPivot 초기값이 서로 달랐고(사람 꺼짐/봇 켜짐), 그대로 두면
    /// 같은 판에서 사람만 맨손이고 봇만 배트를 든 그림이 나온다.
    ///
    /// ★ 예전엔 PlayerMovement.BatPivot / AIPlayerMovement.BatPivot을 타고 들어갔다
    ///   그 둘은 결과 전용 프리팹에서 빠졌으므로 더는 길이 없다. BottomTransform·
    ///   TopTransform과 같이 <b>이름으로</b> 찾는다 — 셋 다 루트의 직계 자식이다.
    /// </summary>
    private static void ApplyBatVisibility(GameObject go)
    {
        Transform bat = go.transform.Find(BatPivotName);
        if (bat == null)
            return;

        bat.gameObject.SetActive(GameState.CurrentGameMode == GameModeType.Push);
    }

    private static Renderer FindJellyRenderer(GameObject root)
    {
        var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null)
            return smr;
        return root.GetComponentInChildren<Renderer>(true);
    }

    // ── 몸 치수의 출처 ──────────────────────────────────────
    //
    // ★ 가로 반지름은 NavMesh 에이전트 타입 설정에서 가져온다
    //   예전엔 인스펙터의 baseRadius(0.5)를 썼는데, 같은 몸의 굵기를 재는 값이
    //   NavMesh 에이전트 타입 설정(0.65)에도 따로 있었다. 출처가 둘이라 한쪽만 고치면
    //   "길찾기가 보는 몸"과 "결과 화면이 보는 몸"이 조용히 어긋난다.
    //   NavMeshUtil이 이미 그 값을 유일한 출처로 감싸고 있으므로 거기서 읽는다.
    //
    // ★ 세로는 가로와 다르다 — 그래서 프리팹의 두 지점을 쓴다
    //   젤리는 구가 아니라 세로로 긴 몸이다(크기 1 기준 반높이 0.807 vs 반지름 0.65).
    //   그런데 카메라는 '중심 = 발밑에서 반지름만큼 위'로 잡고 있었다. 반지름은 가로
    //   치수라 그 점은 실제 중심이 아니었고, 큰 젤리일수록 더 위를 봤다.
    //   프리팹에는 BottomTransform·TopTransform이 이미 있다(바닥 정렬과 이름표가 쓴다).
    //   그 둘의 중점이 진짜 중심이고 간격의 절반이 진짜 반높이다.
    private const string BottomAnchorName = "BottomTransform";
    private const string TopAnchorName = "TopTransform";

    /// <summary>크기 scale인 젤리의 <b>가로</b> 반지름.</summary>
    private static float JellyRadius(float scale)
    {
        return NavMeshUtil.PlayerJellyRadius * scale;
    }

    /// <summary>프리팹의 두 기준점으로 몸의 중심과 반높이를 잰다.</summary>
    private static bool TryMeasureBody(GameObject go, out Vector3 center, out float halfHeight)
    {
        Transform bottom = go.transform.Find(BottomAnchorName);
        Transform top = go.transform.Find(TopAnchorName);

        if (bottom == null || top == null)
        {
            Debug.LogError("[GameResult] " + go.name + " 에 " + BottomAnchorName + "/" + TopAnchorName
                           + " 이 없습니다. 결과 화면의 바닥 정렬과 카메라 조준이 어긋납니다.");
            center = go.transform.position;
            halfHeight = 0f;
            return false;
        }

        center = (bottom.position + top.position) * 0.5f;
        halfHeight = Mathf.Abs(top.position.y - bottom.position.y) * 0.5f;
        return true;
    }

    private static void GroundToFloor(GameObject go, float floorY)
    {
        Transform bottom = go.transform.Find(BottomAnchorName);
        if (bottom == null)
            return;

        go.transform.position += new Vector3(0f, floorY - bottom.position.y, 0f);
    }

    private void SetupNameTag(GameObject root, string playerName)
    {
        var tag = root.GetComponentInChildren<NameTagBillboard>(true);
        if (tag == null)
            return;
        tag.gameObject.SetActive(true);
        tag.SetName(playerName);

        Transform top = root.transform.Find(TopAnchorName);
        if (top != null)
            tag.TopTransform = top;
    }

    private void ApplyJellyColor(GameObject root, Color color)
    {
        var rend = FindJellyRenderer(root);
        if (rend == null)
            return;

        // material은 매번 instance가 생성되도록 .material 접근 (sharedMaterial이 아님)
        var mat = rend.material;
        Color lighter = Color.Lerp(color, Color.white, baseColor02Lightness);
        if (mat.HasProperty(baseColor01Property))
            mat.SetColor(baseColor01Property, color);
        if (mat.HasProperty(baseColor02Property))
            mat.SetColor(baseColor02Property, lighter);
        if (mat.HasProperty(fresnelProperty))
            mat.SetColor(fresnelProperty, color);
    }

    // ──────────────────────────────────────────────
    // Cinemachine 가상 카메라 생성
    // ──────────────────────────────────────────────

    private void BuildVirtualCameras()
    {
        // 각 젤리마다 포커싱용 가상 카메라
        for (int i = 0; i < jellies.Count; i++)
        {
            var jelly = jellies[i];

            //화면에 다 담아야 하는 건 몸 전체다. 세로가 가로보다 길므로(반높이 0.807 vs
            //반지름 0.65, 크기 1 기준) 둘 중 큰 쪽을 감싸는 반지름으로 본다
            TryMeasureBody(jelly, out Vector3 centerWorld, out float halfHeight);
            float radius = Mathf.Max(JellyRadius(jelly.transform.localScale.x), halfHeight);
            float distance = ComputeFocusDistance(radius);

            Vector3 camPos = centerWorld + new Vector3(0f, focusHeightOffset, -distance);
            Quaternion camRot = Quaternion.LookRotation(centerWorld - camPos);

            var go = new GameObject($"VCam_Focus_Rank{displayOrderRanks[i]}");
            go.transform.SetParent(transform, false);
            go.transform.position = camPos;
            go.transform.rotation = camRot;

            var vcam = go.AddComponent<CinemachineCamera>();
            ApplyLens(vcam, virtualCameraFov);
            vcam.Priority = 0;

            focusCams.Add(vcam);
        }

        // 오버뷰 카메라
        Vector3 center = Vector3.zero;
        foreach (var j in jellies) center += j.transform.position;
        if (jellies.Count > 0)
            center /= jellies.Count;

        Vector3 ovPos = center + new Vector3(0f, overviewHeight, -overviewDistance);
        Quaternion ovRot = Quaternion.LookRotation(center - ovPos);
        var ovGo = new GameObject("VCam_Overview");
        ovGo.transform.SetParent(transform, false);
        ovGo.transform.position = ovPos;
        ovGo.transform.rotation = ovRot;
        overviewCam = ovGo.AddComponent<CinemachineCamera>();
        ApplyLens(overviewCam, virtualCameraFov);
        overviewCam.Priority = 0;
    }

    /// <summary>
    /// 반지름 radius인 젤리가 화면에 다 들어오는 <b>최소</b> 거리. 실제로 쓰는 거리는
    /// 이 값과 focusDistanceMultiplier 바닥값 중 큰 쪽이다.
    ///
    /// ★ 최소 거리 식을 r/tan(θ)+r 에서 r/sin(θ) 로 고쳤다
    ///   구는 화면 가장자리에 <b>접할</b> 때 꽉 찬다. 카메라 꼭짓점에서 반각 θ로 뻗는
    ///   프러스텀 옆면까지, 축 위 거리 d에 있는 중심으로부터의 수직거리는 d·sinθ 이므로
    ///   접선 조건은 d·sinθ = r, 즉 <b>d = r / sinθ</b> 다.
    ///
    ///   옛 식 r/tanθ 는 <b>평평한 원판</b>이 화면 높이를 채우는 거리다(그 거리에서
    ///   화면 반높이가 정확히 r). 구에는 깊이가 있으니 +r 로 한 반지름 밀어 중심까지
    ///   맞춘 것인데, 이건 기하가 아니라 어림이다. 실제로 두 값의 비는
    ///     (r/tanθ + r) / (r/sinθ) = cosθ + sinθ
    ///   이고 0&lt;θ&lt;90°에서 이 값은 항상 1보다 크다. 즉 옛 식은 <b>언제나 더 멀었다</b>
    ///   (FoV 40°면 22% 더 멀어 젤리가 그만큼 작게 보인다). 안전한 쪽으로 틀린 셈이라
    ///   잘려 보이지는 않았고, 그래서 오래 남아 있었다.
    ///
    /// ★ 다만 지금은 이 값이 쓰이지 않는다
    ///   아래 Mathf.Max의 다른 항이 radius * focusDistanceMultiplier 인데, 결과 씬 둘 다
    ///   그 값이 7.5다. 이 식은 FoV만으로 정해지는 r의 상수배라(FoV 40°에서 2.92r,
    ///   옛 식은 3.75r) radius가 무엇이든 7.5r 이 항상 이긴다. 즉 젤리 간격을 정하는 건
    ///   전적으로 focusDistanceMultiplier이고, 이 함수는 "그보다 가까워지지는 말라"는
    ///   하한으로만 남아 있다. 그 하한이 실제로 걸리게 하려면 인스펙터의
    ///   focusDistanceMultiplier를 2.92 아래로 내려야 한다.
    ///
    ///   r/sinθ 는 여유가 0인 <b>정확한 접선</b> 값이다. 젤리는 완전한 구가 아니고
    ///   radius도 localScale.x로 어림한 값이라, 하한을 실제로 쓰게 된다면 여기에
    ///   여유 배수를 눈에 보이게 곱하는 편이 낫다 — 옛 +r처럼 식 안에 숨기지 말고.
    /// </summary>
    private float ComputeFocusDistance(float radius)
    {
        if (resultCamera.orthographic)
        {
            resultCamera.orthographicSize = Mathf.Max(resultCamera.orthographicSize, radius * 1.3f);
            return radius * focusDistanceMultiplier;
        }

        float halfFovRad = virtualCameraFov * 0.5f * Mathf.Deg2Rad;
        float exactDistance = radius / Mathf.Sin(halfFovRad);
        return Mathf.Max(radius * focusDistanceMultiplier, exactDistance);
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
        if (jellies.Count == 0)
            yield break;
        // 우측(낮은 순위) → 좌측(높은 순위): jellies[last] = 3위, jellies[0] = 1위
        for (int i = jellies.Count - 1; i >= 0; i--)
        {
            // 이전 활성 카메라들을 0으로 낮추고 현재만 활성화
            DeactivateAllVcams();
            focusCams[i].Priority = 100;

            SetRankText(GetRankString(displayOrderRanks[i]));

            if (!brain.IsBlending)
            {
                yield return new WaitForSeconds(0.5f);
                Animator playerAnimator = jellies[i].GetComponentInChildren<Animator>();
                if (playerAnimator != null)
                    playerAnimator.SetTrigger(AnimParams.Jump);
            }

            // 블렌드 시간 + 머무는 시간
            yield return new WaitForSeconds(cameraTransitionDuration + focusHoldDuration);
        }

        // 마지막: 오버뷰
        DeactivateAllVcams();
        overviewCam.Priority = 100;
        SetRankText(finalText);

        // 블렌딩이 완료될 때까지 대기한 뒤 버튼을 표시한다.
        // 이전 코드에서는 블렌딩 중일 때 yield return null 1회 후 코루틴이 종료되어
        // 버튼이 영원히 나타나지 않았다.
        while (brain.IsBlending)
            yield return null;

        if (buttonRestart != null)
            buttonRestart.SetActive(true);
        if (buttonGameQuit != null)
            buttonGameQuit.SetActive(true);
    }

    private void DeactivateAllVcams()
    {
        foreach (var cam in focusCams)
            if (cam != null)
                cam.Priority = 0;
        if (overviewCam != null)
            overviewCam.Priority = 0;
    }

    private string GetRankString(int rank)
    {
        if (GameState.CurrentGameMode == GameModeType.Push)
            firstPlaceText = "우승!";

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

    // ★ 순위 텍스트를 런타임에 만들던 폴백을 걷어냈다
    //   rankAnnouncementText가 비면 Canvas·CanvasScaler·GraphicRaycaster·TMP까지
    //   코드로 조립했는데, 두 결과 씬 모두 인스펙터에 꽂혀 있어 한 번도 돌지 않았다.
    //   글자 크기·색·앵커가 코드에 박혀 있어서 씬에서 고쳐도 코드가 이긴다고 오해할
    //   여지만 남겼다. 씬에 있는 것을 씬에서 고치게 둔다.

    private void SetRankText(string s)
    {
        if (rankAnnouncementText != null)
            rankAnnouncementText.text = s;
    }
}
