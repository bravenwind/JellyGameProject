// ============================================================
// OffScreenPlayerIndicator.cs
// ============================================================
// 역할: 다른 플레이어/봇의 위치를 화면 위 삼각형으로 표시.
//   - 대상이 화면 밖이면 화면 테두리(여백 안쪽)를 따라 움직이며 그 방향을 가리킨다.
//   - 대상이 화면 안이면 그 머리 위에 삼각형을 띄운다(아래를 가리킴).
//   - 삼각형 색은 해당 플레이어의 현재 색(DisplayColor / 봇 머티리얼 색)과 연동된다.
//
// [특징]
//   - 씬 배치/에디터 와이어링이 필요 없도록 런타임에 캔버스와 삼각형 스프라이트를
//     스스로 생성하고, RuntimeInitializeOnLoadMethod로 자동 부트스트랩한다.
//   - 인디케이터는 풀링(딕셔너리 재사용)되며 대상이 사라지면 정리된다.
//
// ★ 한때 삭제됐다가 되살린 파일
//   Photon을 걷어낼 때 `using Photon.Pun;` 한 줄만 보고 통째로 지웠는데,
//   실제로 Photon API를 쓰는 곳은 하나도 없었다(이미 LAN으로 이식돼 있었다).
//   증상은 "화면 테두리의 다른 플레이어 표시가 사라짐"이었다.
//   using 하나로 파일의 생사를 판단하면 안 된다는 기록으로 남긴다.
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
    [Tooltip("삼각형 한 변 길이")]
    [SerializeField] private float indicatorSize = 46f;

    [Header("월드")]
    [Tooltip("대상 머리 기준 높이(스케일에 비례해 가감)")]
    [SerializeField] private float worldHeadHeight = 1.2f;

    [Header("렌더")]
    [Tooltip("인디케이터 캔버스 정렬 순서(클수록 위에 그려짐)")]
    [SerializeField] private int sortingOrder = 50;

    // ─────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────
    private static OffScreenPlayerIndicator instance;

    private Camera cam;
    private Canvas canvas;
    private RectTransform canvasRect;
    private Sprite triangleSprite;

    private class Indicator
    {
        public RectTransform rect;
        public Image image;

        //사람이든 봇이든 여기 하나로 들어온다. 색·판밖 여부를 INetEntity가 답해준다
        public INetEntity entity;
    }

    private readonly Dictionary<Transform, Indicator> active = new Dictionary<Transform, Indicator>();
    private readonly Queue<Indicator> pool = new Queue<Indicator>();
    private readonly List<Transform> staleKeys = new List<Transform>();
    private readonly HashSet<Transform> seenThisFrame = new HashSet<Transform>();

    // ─────────────────────────────────────────────────────────
    // 자동 부트스트랩 (씬 배치 불필요)
    // ─────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;
        var go = new GameObject("OffScreenPlayerIndicator");
        instance = go.AddComponent<OffScreenPlayerIndicator>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        BuildCanvas();
        triangleSprite = CreateTriangleSprite();
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

        // ★ 예전엔 사람 목록·봇 목록을 따로 돌았다
        //   본문이 '화면 밖이면 테두리에 삼각형을 띄운다'로 똑같은데 색을 읽는 줄만
        //   갈라져 있었다(LanPlayerState.VisualColor / 봇 렌더러 직접 조회).
        //   INetEntity가 그 차이를 안으로 삼켰으므로 한 벌이면 된다.
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

            Indicator ind = GetOrCreate(e.Transform);
            ind.entity = e;
            UpdateIndicator(ind, e.Transform);
            seenThisFrame.Add(e.Transform);
        }

        CleanupStale();
    }

    // ─────────────────────────────────────────────────────────
    // 개별 인디케이터 위치/회전/색 갱신
    // ─────────────────────────────────────────────────────────
    private void UpdateIndicator(Indicator ind, Transform target)
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

        if (!ind.rect.gameObject.activeSelf)
            ind.rect.gameObject.SetActive(true);
        ind.rect.position = new Vector3(pos.x, pos.y, 0f);
        ind.rect.localRotation = Quaternion.Euler(0f, 0f, angle);

        Color c = GetColor(ind);
        c.a = 1f;
        ind.image.color = c;
    }

    //사람·봇 모두 INetEntity.VisualColor 하나로 답한다
    private static Color GetColor(Indicator ind)
    {
        return ind.entity != null ? ind.entity.VisualColor : Color.white;
    }

    // ─────────────────────────────────────────────────────────
    // 풀링 / 정리
    // ─────────────────────────────────────────────────────────
    private Indicator GetOrCreate(Transform key)
    {
        if (active.TryGetValue(key, out var existing))
            return existing;

        Indicator ind = pool.Count > 0 ? pool.Dequeue() : CreateIndicator();
        active[key] = ind;
        return ind;
    }

    private Indicator CreateIndicator()
    {
        var go = new GameObject("PlayerIndicator", typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(canvasRect, false);
        rect.sizeDelta = new Vector2(indicatorSize, indicatorSize);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var img = go.AddComponent<Image>();
        img.sprite = triangleSprite;
        img.raycastTarget = false;

        // 어두운 배경에서도 잘 보이도록 외곽선
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
        outline.effectDistance = new Vector2(2f, -2f);

        return new Indicator { rect = rect, image = img };
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
            Release(staleKeys[i]);
    }

    private void Release(Transform key)
    {
        if (!active.TryGetValue(key, out var ind))
            return;
        active.Remove(key);
        ind.entity = null;

        if (ind.rect != null)
            ind.rect.gameObject.SetActive(false);

        pool.Enqueue(ind);
    }

    private void HideAll()
    {
        staleKeys.Clear();
        foreach (var kvp in active)
            staleKeys.Add(kvp.Key);
        for (int i = 0; i < staleKeys.Count; i++)
            Release(staleKeys[i]);
    }

    // ─────────────────────────────────────────────────────────
    // 런타임 캔버스 / 삼각형 스프라이트 생성
    // ─────────────────────────────────────────────────────────
    private void BuildCanvas()
    {
        var go = new GameObject("OffScreenIndicatorCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);

        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        // 픽셀 단위 위치/크기를 그대로 쓰기 위해 상수 픽셀 모드
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        canvasRect = go.GetComponent<RectTransform>();
    }

    /// <summary>꼭짓점이 위를 향하는 꽉 찬 삼각형 스프라이트를 런타임 생성(흰색, 색은 tint로 입힘).</summary>
    private static Sprite CreateTriangleSprite()
    {
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[s * s];
        Color32 clear = new Color32(255, 255, 255, 0);
        Color32 white = new Color32(255, 255, 255, 255);

        for (int y = 0; y < s; y++)
        {
            // y=0(바닥)에서 폭 최대, y=s-1(위 꼭짓점)에서 폭 0
            float fromTop = (float)(s - 1 - y) / (s - 1); // 0(위)~1(아래)
            float halfWidth = fromTop * (s * 0.5f);
            float cx = s * 0.5f;
            for (int x = 0; x < s; x++)
            {
                pixels[y * s + x] = (Mathf.Abs(x + 0.5f - cx) <= halfWidth) ? white : clear;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
