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
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

public class OffScreenPlayerIndicator : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // 설정
    // ─────────────────────────────────────────────────────────
    [Header("표시 대상")]
    [Tooltip("AI 봇도 삼각형으로 표시할지 여부")]
    public bool includeBots = true;

    [Header("레이아웃 (픽셀)")]
    [Tooltip("화면 테두리에서 안쪽으로 띄울 여백")]
    public float edgeMargin = 60f;
    [Tooltip("화면 안에 있을 때 머리 위로 띄울 높이")]
    public float onScreenHeadOffset = 42f;
    [Tooltip("삼각형 한 변 길이")]
    public float indicatorSize = 46f;

    [Header("월드")]
    [Tooltip("대상 머리 기준 높이(스케일에 비례해 가감)")]
    public float worldHeadHeight = 1.2f;

    [Header("렌더")]
    [Tooltip("인디케이터 캔버스 정렬 순서(클수록 위에 그려짐)")]
    public int sortingOrder = 50;

    // ─────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────
    private static OffScreenPlayerIndicator _instance;

    private Camera _cam;
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private Sprite _triangleSprite;

    private class Indicator
    {
        public RectTransform rect;
        public Image image;
        public JellyNet.LanPlayerState player; // 사람 플레이어면 설정
        public AIPlayerMovement bot;     // 봇이면 설정
        public Renderer botRenderer;     // 봇 색 조회용 캐시
    }

    private readonly Dictionary<Transform, Indicator> _active = new Dictionary<Transform, Indicator>();
    private readonly Queue<Indicator> _pool = new Queue<Indicator>();
    private readonly List<Transform> _staleKeys = new List<Transform>();
    private readonly HashSet<Transform> _seenThisFrame = new HashSet<Transform>();

    // ─────────────────────────────────────────────────────────
    // 자동 부트스트랩 (씬 배치 불필요)
    // ─────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("OffScreenPlayerIndicator");
        _instance = go.AddComponent<OffScreenPlayerIndicator>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        BuildCanvas();
        _triangleSprite = CreateTriangleSprite();
    }

    // ─────────────────────────────────────────────────────────
    // 갱신 (카메라 이동 후 위치 잡도록 LateUpdate)
    // ─────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        // 게임 중이 아니거나 카메라가 없으면 전부 숨김
        if (GameState.Phase != GamePhase.Playing)
        {
            if (_active.Count > 0) HideAll();
            return;
        }

        if (_cam == null) _cam = Camera.main;
        if (_cam == null) { HideAll(); return; }

        _seenThisFrame.Clear();

        // ── 사람 플레이어 ──
        var players = EntityRegistry.Players;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null) continue;
            // [LAN 이식] 예전엔 photonView.IsMine을 봤는데, photonView가 항상 null이 되면서
            //   이 continue가 한 번도 안 걸렸다 → 내 화살표가 나에게도 표시된다.
            if (p.IsMine) continue; // 내 자신 제외
            if (p.IsOutOfPlay) continue; // 탈락/흡수 판정 단일 출처 (G6)

            var ind = GetOrCreate(p.transform);
            ind.player = p;
            ind.bot = null;
            ind.botRenderer = null;
            UpdateIndicator(ind, p.transform);
            _seenThisFrame.Add(p.transform);
        }

        // ── AI 봇 ──
        if (includeBots)
        {
            var bots = EntityRegistry.Bots;
            for (int i = 0; i < bots.Count; i++)
            {
                var b = bots[i];
                if (b == null || b.IsOutOfPlay) continue; // 탈락/흡수 판정 단일 출처 (G6)

                var ind = GetOrCreate(b.transform);
                ind.bot = b;
                ind.player = null;
                if (ind.botRenderer == null)
                    ind.botRenderer = b.GetComponentInChildren<Renderer>();
                UpdateIndicator(ind, b.transform);
                _seenThisFrame.Add(b.transform);
            }
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
        Vector3 sp = _cam.WorldToScreenPoint(headWorld);

        bool behind = sp.z < 0f;
        Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 sp2;

        if (behind)
        {
            // 카메라 뒤에 있는 대상은 WorldToScreenPoint가 돌려주는 화면 좌표가
            // 상하/좌우로 뒤집힌다. 단순히 중심 기준으로 미러링하면 방향이 틀어져
            // (예: 카메라 아래쪽 대상이 화면 위 테두리에 뜨는 버그) 발생한다.
            // → 월드 오프셋을 카메라의 right/up 축에 투영해 화면상의 실제 방향을 직접 구한다.
            Vector3 dirWorld = headWorld - _cam.transform.position;
            float rx = Vector3.Dot(dirWorld, _cam.transform.right);
            float ry = Vector3.Dot(dirWorld, _cam.transform.up);
            Vector2 d = new Vector2(rx, ry);
            if (d.sqrMagnitude < 1e-4f) d = Vector2.down;
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
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;

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

        if (!ind.rect.gameObject.activeSelf) ind.rect.gameObject.SetActive(true);
        ind.rect.position = new Vector3(pos.x, pos.y, 0f);
        ind.rect.localRotation = Quaternion.Euler(0f, 0f, angle);

        Color c = GetColor(ind);
        c.a = 1f;
        ind.image.color = c;
    }

    private Color GetColor(Indicator ind)
    {
        // [LAN 이식] DisplayColor는 아무도 채우지 않는 값이라 항상 흰색이었다.
        //   실제 렌더러 색을 읽는다(봇과 동일한 방식).
        if (ind.player != null) return ind.player.VisualColor;
        // 읽기 전용 조회이므로 sharedMaterial 사용 — .material은 봇마다 머티리얼 인스턴스를 복제해
        // 배칭을 깨뜨린다(리더보드 GameModeManager.GetBotColor와 동일 패턴). (G4)
        if (ind.botRenderer != null && ind.botRenderer.sharedMaterial != null
            && ind.botRenderer.sharedMaterial.HasProperty("_FresnelColor"))
            return ind.botRenderer.sharedMaterial.GetColor("_FresnelColor");
        return Color.white;
    }

    // ─────────────────────────────────────────────────────────
    // 풀링 / 정리
    // ─────────────────────────────────────────────────────────
    private Indicator GetOrCreate(Transform key)
    {
        if (_active.TryGetValue(key, out var existing)) return existing;

        Indicator ind = _pool.Count > 0 ? _pool.Dequeue() : CreateIndicator();
        _active[key] = ind;
        return ind;
    }

    private Indicator CreateIndicator()
    {
        var go = new GameObject("PlayerIndicator", typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(_canvasRect, false);
        rect.sizeDelta = new Vector2(indicatorSize, indicatorSize);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var img = go.AddComponent<Image>();
        img.sprite = _triangleSprite;
        img.raycastTarget = false;

        // 어두운 배경에서도 잘 보이도록 외곽선
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
        outline.effectDistance = new Vector2(2f, -2f);

        return new Indicator { rect = rect, image = img };
    }

    private void CleanupStale()
    {
        _staleKeys.Clear();
        foreach (var kvp in _active)
        {
            if (kvp.Key == null || !_seenThisFrame.Contains(kvp.Key))
                _staleKeys.Add(kvp.Key);
        }
        for (int i = 0; i < _staleKeys.Count; i++)
            Release(_staleKeys[i]);
    }

    private void Release(Transform key)
    {
        if (!_active.TryGetValue(key, out var ind)) return;
        _active.Remove(key);
        ind.player = null;
        ind.bot = null;
        ind.botRenderer = null;
        if (ind.rect != null) ind.rect.gameObject.SetActive(false);
        _pool.Enqueue(ind);
    }

    private void HideAll()
    {
        _staleKeys.Clear();
        foreach (var kvp in _active) _staleKeys.Add(kvp.Key);
        for (int i = 0; i < _staleKeys.Count; i++)
            Release(_staleKeys[i]);
    }

    // ─────────────────────────────────────────────────────────
    // 런타임 캔버스 / 삼각형 스프라이트 생성
    // ─────────────────────────────────────────────────────────
    private void BuildCanvas()
    {
        var go = new GameObject("OffScreenIndicatorCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);

        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = sortingOrder;

        // 픽셀 단위 위치/크기를 그대로 쓰기 위해 상수 픽셀 모드
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        _canvasRect = go.GetComponent<RectTransform>();
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
        if (_instance == this) _instance = null;
    }
}
