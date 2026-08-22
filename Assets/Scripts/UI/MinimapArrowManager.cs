// ============================================================
// MinimapArrowManager.cs
// ============================================================
// 역할: 미니맵에 플레이어/AI 화살표를 자동으로 생성·관리
//
// [동작 방식]
//   - arrowPrefab을 씬에 있는 LanPlayerState(플레이어) 및
//     AIPlayerMovement(봇) 오브젝트마다 하나씩 인스턴스화
//   - 로컬 플레이어 → 초록색 / 나머지 → 빨간색
//   - 매 프레임 새로 추가된 엔티티나 파괴된 엔티티를 감지해 화살표 갱신
//
// [사용 방법]
//   1. 빈 GameObject에 이 스크립트를 붙인다.
//   2. arrowPrefab에 MinimapArrow 컴포넌트가 있는 화살표 프리팹 할당.
//   3. 프리팹의 오브젝트(및 자식들)의 레이어를 Minimap 전용 레이어로 설정.
//   4. 미니맵 카메라의 Culling Mask에서 해당 레이어만(또는 포함하여) 보이게 설정.
// ============================================================

using System.Collections.Generic;
using UnityEngine;

public class MinimapArrowManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // 인스펙터 설정
    // ─────────────────────────────────────────────────────────
    [Header("화살표 프리팹 (MinimapArrow 컴포넌트 포함)")]
    public GameObject arrowPrefab;

    [Header("색상")]
    public Color localPlayerColor  = Color.green;
    public Color remotePlayerColor = Color.red;
    public Color botColor          = Color.red;

    [Header("화살표 높이 오프셋 (미니맵 카메라 아래에 위치)")]
    [Tooltip("미니맵 카메라보다 조금 낮게 두면 화살표가 카메라에 잘 잡힙니다.")]
    public Vector3 arrowOffset = new Vector3(0f, 1f, 0f);

    [Header("미니맵 레이어 이름")]
    [Tooltip("화살표를 배치할 레이어 이름. 미니맵 카메라의 Culling Mask와 맞춰야 합니다.")]
    public string minimapLayerName = "Minimap";

    // ─────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────
    // key = 추적 대상 Transform, value = 생성된 화살표 GameObject
    private Dictionary<Transform, GameObject> _arrows = new Dictionary<Transform, GameObject>();

    // 스캔 주기 (매 프레임마다 FindObjectsByType을 하면 비용이 크므로 0.5초마다)
    private float _scanInterval = 0.5f;
    private float _scanTimer    = 0f;

    // ─────────────────────────────────────────────────────────
    // 초기화 & 업데이트
    // ─────────────────────────────────────────────────────────
    private void Start()
    {
        ScanAndRefresh();
    }

    private void Update()
    {
        _scanTimer += Time.deltaTime;
        if (_scanTimer >= _scanInterval)
        {
            _scanTimer = 0f;
            ScanAndRefresh();
        }

        // 대상이 파괴된 화살표 정리
        CleanupDestroyedTargets();
    }

    // ─────────────────────────────────────────────────────────
    // 씬 스캔 및 화살표 생성
    // ─────────────────────────────────────────────────────────
    private void ScanAndRefresh()
    {
        // ── 사람 플레이어 ──
        //EntityRegistry는 OnEnable에서 등록된 목록이라 씬 전체를 훑지 않는다.
        //변경이 없으면 같은 스냅샷을 재사용하므로 GC 할당도 없다
        foreach (var player in EntityRegistry.Players)
        {
            if (player == null) continue;
            Transform tf = player.transform;

            if (!_arrows.ContainsKey(tf))
            {
                // 로컬 판별은 NetIdentity로 한다.
                //   틀리면 내 화살표가 원격 색으로 나오고 미니맵 카메라가 아무도 안 따라간다.
                bool isLocal = player.IsMine;
                Color color  = isLocal ? localPlayerColor : remotePlayerColor;
                if (isLocal)
                    BindMinimapCamera(player.transform);
                SpawnArrow(tf, color);
            }
        }

        // ── 봇 ──
        foreach (var bot in EntityRegistry.Bots)
        {
            if (bot == null) continue;
            Transform tf = bot.transform;

            if (!_arrows.ContainsKey(tf))
            {
                SpawnArrow(tf, botColor);
            }
        }
    }

    //태그가 없거나 컴포넌트가 빠져 있으면 조용히 넘어간다.
    //예전엔 한 줄로 이어 붙여서 태그가 없으면 여기서 NullReference가 났다
    private void BindMinimapCamera(Transform target)
    {
        GameObject cam = GameObject.FindGameObjectWithTag("MinimapCamera");
        if (cam == null)
            return;

        TopDownCameraFollow follow = cam.GetComponent<TopDownCameraFollow>();
        if (follow != null)
            follow.target = target;
    }

    // ─────────────────────────────────────────────────────────
    // 화살표 생성
    // ─────────────────────────────────────────────────────────
    private void SpawnArrow(Transform target, Color color)
    {
        if (arrowPrefab == null)
        {
            Debug.LogWarning("[MinimapArrowManager] arrowPrefab이 할당되지 않았습니다.");
            return;
        }

        GameObject arrow = Instantiate(arrowPrefab, target.position + arrowOffset, Quaternion.identity);
        arrow.name = $"MinimapArrow_{target.name}";

        // 레이어 설정
        int layer = LayerMask.NameToLayer(minimapLayerName);
        if (layer >= 0)
        {
            SetLayerRecursively(arrow, layer);
        }
        else
        {
            Debug.LogWarning($"[MinimapArrowManager] '{minimapLayerName}' 레이어를 찾을 수 없습니다. Project Settings > Tags and Layers에서 추가하세요.");
        }

        // MinimapArrow 컴포넌트 설정
        MinimapArrow minimapArrow = arrow.GetComponent<MinimapArrow>();
        if (minimapArrow != null)
        {
            minimapArrow.target = target;
            minimapArrow.offset = arrowOffset;
            minimapArrow.SetColor(color);
        }
        else
        {
            Debug.LogWarning("[MinimapArrowManager] arrowPrefab에 MinimapArrow 컴포넌트가 없습니다.");
        }

        _arrows[target] = arrow;
    }

    // ─────────────────────────────────────────────────────────
    // 파괴된 대상 정리
    // ─────────────────────────────────────────────────────────
    private void CleanupDestroyedTargets()
    {
        List<Transform> toRemove = null;

        foreach (var kvp in _arrows)
        {
            // Transform이 null이면 오브젝트가 파괴된 것
            if (kvp.Key == null)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
                if (toRemove == null) toRemove = new List<Transform>();
                toRemove.Add(kvp.Key);
            }
        }

        if (toRemove != null)
        {
            foreach (var key in toRemove)
                _arrows.Remove(key);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 유틸: 레이어 재귀 설정
    // ─────────────────────────────────────────────────────────
    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // ─────────────────────────────────────────────────────────
    // 외부 API: 특정 대상의 화살표 색상 변경 (선택 사항)
    // ─────────────────────────────────────────────────────────
}
