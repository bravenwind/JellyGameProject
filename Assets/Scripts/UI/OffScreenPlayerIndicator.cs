// ============================================================
// OffScreenPlayerIndicator.cs
// ============================================================
// 역할: 다른 플레이어/봇의 위치를 화면 위 삼각형으로 표시.
//   - 대상이 화면 밖이면 화면 테두리(여백 안쪽)를 따라 움직이며 그 방향을 가리킨다.
//   - 대상이 화면 안이면 그 머리 위에 삼각형을 띄운다(아래를 가리킴).
//   - 삼각형 색은 해당 플레이어의 현재 색(DisplayColor / 봇 머티리얼 색)과 연동된다.
//
// [특징]
//   - 이 스크립트가 정하는 건 <b>어디에 놓을지</b>뿐이다. 삼각형의 생김새는 프리팹이,
//     담을 자리는 씬의 캔버스가 갖고 있다.
//   - 대상 하나당 삼각형 하나를 Instantiate하고, 대상이 사라지면 Destroy한다.
//
// ★ 한때 삭제됐다가 되살린 파일
//   Photon을 걷어낼 때 `using Photon.Pun;` 한 줄만 보고 통째로 지웠는데,
//   실제로 Photon API를 쓰는 곳은 하나도 없었다(이미 LAN으로 이식돼 있었다).
//   증상은 "화면 테두리의 다른 플레이어 표시가 사라짐"이었다.
//   using 하나로 파일의 생사를 판단하면 안 된다는 기록으로 남긴다.
//
// ★ 예전엔 RuntimeInitializeOnLoadMethod + DontDestroyOnLoad로 스스로 태어났다
//   "씬 배치가 필요 없게"가 목적이었는데, 대가가 컸다:
//     · 타이틀·로비·로딩·결과 씬에서도 캔버스와 LateUpdate를 계속 들고 있었다
//       (실제로 그리는 건 GamePhase.Playing일 때뿐인데도)
//     · 그걸 지탱하려고 static instance · 중복 가드 · OnDestroy 정리가 딸려왔다
//     · [SerializeField] 여섯 개가 <b>인스펙터에서 만질 수 없었다</b> —
//       AddComponent로 생기는 오브젝트라 직렬화된 값이 들어올 자리가 없다
//
//   같은 일을 하는 MinimapArrowManager는 처음부터 두 게임 씬에 배치된 평범한
//   컴포넌트였다. 이 파일만 예외였던 셈이라 그쪽에 맞췄다.
//   (프로젝트의 다른 RuntimeInitializeOnLoadMethod 7곳은 전부 SubsystemRegistration —
//    도메인 리로드 때 static을 되돌리는 용도지 오브젝트를 만드는 용도가 아니다)
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using JellyNet;

public class OffScreenPlayerIndicator : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // 설정
    // ─────────────────────────────────────────────────────────
    [Header("표시 대상")]
    [Tooltip("AI 봇도 삼각형으로 표시할지 여부")]
    [SerializeField] private bool includeBots = true;

    [Header("레이아웃 (픽셀)")]
    [Tooltip("화면 테두리에서 안쪽으로 띄울 여백")]
    [SerializeField] private float edgeMargin = 60f;
    [Tooltip("화면 안에 있을 때 머리 위로 띄울 높이")]
    [SerializeField] private float onScreenHeadOffset = 42f;

    [Header("월드")]
    [Tooltip("대상 머리 기준 높이(스케일에 비례해 가감)")]
    [SerializeField] private float worldHeadHeight = 1.2f;

    [Header("렌더")]
    [Tooltip("삼각형을 담을 캔버스. 자식 OffScreenIndicatorCanvas")]
    [SerializeField] private RectTransform canvasRect;

    // ★ 삼각형은 프리팹이다 — 코드가 조립하지 않는다
    //   예전엔 CreateIndicator()가 GameObject를 만들고 Image·Outline을 붙이고
    //   스프라이트·크기·외곽선 색까지 코드로 정했다. 그래서 삼각형 하나의 생김새를
    //   바꾸려면 스크립트를 고쳐야 했고, indicatorSize 같은 값이 관리자에 섞여 있었다.
    //   MinimapArrowManager가 이미 arrowPrefab + MinimapArrow 구조다 — 거기 맞췄다.
    [Tooltip("삼각형 프리팹. Prefabs/UI/PlayerIndicator")]
    [SerializeField] private PlayerIndicator indicatorPrefab;

    // ─────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────
    private Camera cam;

    private readonly Dictionary<Transform, PlayerIndicator> active = new Dictionary<Transform, PlayerIndicator>();
    private readonly List<Transform> staleKeys = new List<Transform>();
    private readonly HashSet<Transform> seenThisFrame = new HashSet<Transform>();

    // MinimapArrowManager와 같은 시점에 준비한다 — 둘 다 게임 씬 컴포넌트고
    // 첫 LateUpdate보다 Start가 먼저 돌아서 canvasRect가 비어 있을 일이 없다.
    // 연결이 비어 있으면 조용히 안 그리는 대신 소리를 낸다 —
    // 이 파일은 예전에 소리 없이 사라진 전력이 있다.
    private void Start()
    {
        if (indicatorPrefab == null || canvasRect == null)
        {
            Debug.LogError("[화면밖표시] 인스펙터 연결이 비어 있습니다 — "
                + "Canvas Rect는 자식 OffScreenIndicatorCanvas, "
                + "Indicator Prefab은 Prefabs/UI/PlayerIndicator. 표시를 끕니다.", this);
            enabled = false;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 갱신 (카메라 이동 후 위치 잡도록 LateUpdate)
    // ─────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        // 게임 중이 아니거나 카메라가 없으면 전부 숨김
        if (GameState.Phase != GamePhase.Playing)
        {
            if (active.Count > 0)
                HideAll();
            return;
        }

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
        {
            HideAll();
            return;
        }

        seenThisFrame.Clear();

        IReadOnlyList<INetEntity> entities = EntityRegistry.Entities;

        for (int i = 0; i < entities.Count; i++)
        {
            INetEntity e = entities[i];

            if (e == null || e.Transform == null)
                continue;
            if (e.IsOutOfPlay)
                continue;                       // 탈락·흡수 중인 상대는 표시하지 않는다
            if (e.IsBot && !includeBots)
                continue;

            //내 캐릭터는 화살표가 필요 없다. 봇은 호스트에서 IsMine이 참이므로 IsBot을 먼저 본다
            if (!e.IsBot && e.Identity != null && e.Identity.IsMine)
                continue;

            PlayerIndicator ind = GetOrCreate(e.Transform);
            ind.Entity = e;
            UpdateIndicator(ind, e.Transform);
            seenThisFrame.Add(e.Transform);
        }

        CleanupStale();
    }

    // ─────────────────────────────────────────────────────────
    // 개별 인디케이터 위치/회전/색 갱신
    // ─────────────────────────────────────────────────────────
    private void UpdateIndicator(PlayerIndicator ind, Transform target)
    {
        float scale = target.localScale.y;
        Vector3 headWorld = target.position + Vector3.up * (worldHeadHeight * scale);
        Vector3 sp = cam.WorldToScreenPoint(headWorld);

        bool behind = sp.z < 0f;
        Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 sp2;

        if (behind)
        {
            // 카메라 뒤에 있는 대상은 WorldToScreenPoint가 돌려주는 화면 좌표가
            // 상하/좌우로 뒤집힌다. 단순히 중심 기준으로 미러링하면 방향이 틀어져
            // (예: 카메라 아래쪽 대상이 화면 위 테두리에 뜨는 버그) 발생한다.
            // → 월드 오프셋을 카메라의 right/up 축에 투영해 화면상의 실제 방향을 직접 구한다.
            Vector3 dirWorld = headWorld - cam.transform.position;
            float rx = Vector3.Dot(dirWorld, cam.transform.right);
            float ry = Vector3.Dot(dirWorld, cam.transform.up);
            Vector2 d = new Vector2(rx, ry);
            if (d.sqrMagnitude < 1e-4f)
                d = Vector2.down;
            sp2 = center + d.normalized * Mathf.Max(Screen.width, Screen.height);
        }
        else
        {
            sp2 = new Vector2(sp.x, sp.y);
        }

        bool onScreen = !behind
            && sp.x >= edgeMargin && sp.x <= Screen.width - edgeMargin
            && sp.y >= edgeMargin && sp.y <= Screen.height - edgeMargin;

        Vector2 pos;
        Vector2 pointDir; // 삼각형 꼭짓점이 향할 방향

        if (onScreen)
        {
            // 머리 위에 띄우고 아래(대상)를 가리킴
            pos = new Vector2(sp.x, sp.y + onScreenHeadOffset);
            pointDir = Vector2.down;
        }
        else
        {
            // 화면 중심 → 대상 방향으로 테두리(여백 안쪽)에 클램프
            Vector2 dir = sp2 - center;
            if (dir.sqrMagnitude < 1e-4f)
                dir = Vector2.up;

            float halfW = Screen.width * 0.5f - edgeMargin;
            float halfH = Screen.height * 0.5f - edgeMargin;
            float absX = Mathf.Abs(dir.x);
            float absY = Mathf.Abs(dir.y);
            float sX = absX > 1e-4f ? halfW / absX : float.MaxValue;
            float sY = absY > 1e-4f ? halfH / absY : float.MaxValue;
            float t = Mathf.Min(sX, sY);

            pos = center + dir * t;
            pointDir = dir.normalized;
        }

        // 기본 스프라이트는 꼭짓점이 위(+y)를 향함 → 방향에 맞춰 회전
        float angle = Mathf.Atan2(pointDir.y, pointDir.x) * Mathf.Rad2Deg - 90f;

        //예전엔 여기서 SetActive(true)를 확인했다. 풀에서 꺼낸 것이 꺼져 있을 수 있어서였는데,
        //이제는 만들자마자 쓰고 놓을 때 없애므로 꺼진 인디케이터가 존재하지 않는다.
        ind.Rect.position = new Vector3(pos.x, pos.y, 0f);
        ind.Rect.localRotation = Quaternion.Euler(0f, 0f, angle);

        //색은 프리팹이 자기 Image를 알고 있으므로 거기에 맡긴다.
        //예전엔 여기서 ind.image.color를 직접 건드렸다 — 관리자가 삼각형의 내부 구조까지 알았다.
        ind.ApplyColor();
    }

    // ─────────────────────────────────────────────────────────
    // 생성 / 정리
    // ─────────────────────────────────────────────────────────
    private PlayerIndicator GetOrCreate(Transform key)
    {
        if (active.TryGetValue(key, out var existing))
            return existing;

        //위치·회전은 바로 아래 UpdateIndicator가 정하므로 여기서는 부모만 잡아준다.
        //
        // ★ 세 번째 인자 false(worldPositionStays)를 반드시 넘긴다 —
        //   빼먹으면 플레이어마다 삼각형 크기가 달라진다.
        //   Instantiate(원본, 부모)는 worldPositionStays가 true다. 그러면 유니티는
        //   "월드 스케일을 프리팹 그대로 유지"하려고 자식의 localScale에
        //   프리팹스케일 / 부모의 lossyScale 을 넣는다.
        //   그런데 이 부모는 Screen Space - Overlay 캔버스라 스케일을 캔버스 시스템이
        //   매 프레임 다시 써 넣는다. 그 값이 아직 안 들어온 첫 프레임에는 lossyScale이
        //   씬에 적힌 값(0)이라, 그때 태어난 인디케이터만 엉뚱한 크기를 갖고 굳었다.
        //   나중에 접속·부활한 상대의 삼각형은 정상 크기 → "쟤만 크다"가 된다.
        //   false를 주면 프리팹의 localScale(1,1,1)이 그대로 들어와 시점과 무관해진다.
        PlayerIndicator indicator = Instantiate(indicatorPrefab, canvasRect, false);
        active[key] = indicator;
        return indicator;
    }

    private void CleanupStale()
    {
        staleKeys.Clear();
        foreach (var kvp in active)
        {
            if (kvp.Key == null || !seenThisFrame.Contains(kvp.Key))
                staleKeys.Add(kvp.Key);
        }
        for (int i = 0; i < staleKeys.Count; i++)
            CleanupActive(staleKeys[i]);
    }

    private void CleanupActive(Transform key)
    {
        if (!active.TryGetValue(key, out var ind))
            return;

        active.Remove(key);

        if (ind != null)
            Destroy(ind.gameObject);
    }

    private void HideAll()
    {
        staleKeys.Clear();
        foreach (var kvp in active)
            staleKeys.Add(kvp.Key);
        for (int i = 0; i < staleKeys.Count; i++)
            CleanupActive(staleKeys[i]);
    }
}
