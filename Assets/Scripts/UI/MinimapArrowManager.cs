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
//   3. 프리팹의 오브젝트(및 자식들)의 레이어를 Minimap으로 설정. <b>코드는 손대지 않는다.</b>
//   4. 미니맵 카메라의 Culling Mask에서 해당 레이어가 보이게, 메인 카메라에서는 안 보이게 설정.
//      (현재 씬: MinimapCamera는 레이어 10 포함, 메인 Camera는 9·10 제외)
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using JellyNet;

public class MinimapArrowManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // 인스펙터 설정
    // ─────────────────────────────────────────────────────────
    [Header("화살표 프리팹 (MinimapArrow 컴포넌트 포함)")]
    [SerializeField] private GameObject arrowPrefab;

    [Header("색상")]
    [SerializeField] private Color localPlayerColor  = Color.green;
    [SerializeField] private Color remotePlayerColor = Color.red;
    [SerializeField] private Color botColor          = Color.red;

    [Header("화살표 높이 오프셋 (미니맵 카메라 아래에 위치)")]
    [Tooltip("미니맵 카메라보다 조금 낮게 두면 화살표가 카메라에 잘 잡힙니다.")]
    [SerializeField] private Vector3 arrowOffset = new Vector3(0f, 1f, 0f);

    // ★ 레이어는 코드가 정하지 않는다 — 프리팹에 이미 박혀 있다
    //   예전엔 minimapLayerName("Minimap") 문자열을 받아 스폰할 때마다
    //   SetLayerRecursively로 화살표와 자식을 훑으며 레이어를 칠했다.
    //   그런데 프리팹의 루트도 자식(MiniMapIcon)도 <b>이미 Minimap 레이어</b>였다.
    //   같은 값을 다시 칠하는 코드였다.
    //
    //   인스펙터에서 한 번 정해두면 되는 것을 코드로 다시 정하면 출처가 둘이 된다 —
    //   프리팹을 고쳐도 코드가 덮어써서 "왜 안 바뀌지"가 되고, 반대로 코드만 고치면
    //   프리팹을 보는 사람이 속는다. 세팅은 세팅이 있는 곳에 둔다.

    // ─────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────
    // key = 추적 대상 Transform, value = 생성된 화살표 GameObject
    private Dictionary<Transform, GameObject> arrows = new Dictionary<Transform, GameObject>();

    // 스캔 주기 (매 프레임마다 FindObjectsByType을 하면 비용이 크므로 0.5초마다)
    private float scanInterval = 0.5f;
    private float scanTimer    = 0f;

    // ─────────────────────────────────────────────────────────
    // 초기화 & 업데이트
    // ─────────────────────────────────────────────────────────
    private void Start()
    {
        ScanAndRefresh();
    }

    private void Update()
    {
        scanTimer += Time.deltaTime;
        if (scanTimer >= scanInterval)
        {
            scanTimer = 0f;
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
        //EntityRegistry는 OnEnable에서 등록된 목록이라 씬 전체를 훑지 않는다.
        //변경이 없으면 같은 스냅샷을 재사용하므로 GC 할당도 없다
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null)
                continue;
            Transform tf = e.Transform;

            if (arrows.ContainsKey(tf))
                continue;

            // ★ IsMine만 보면 안 된다
            //   봇의 소유자는 호스트(1)라, 호스트 화면에서는 봇도 Identity.IsMine이 true다.
            //   그대로 쓰면 봇 화살표가 초록으로 나오고 미니맵 카메라가 봇을 따라간다.
            //   "내 캐릭터"는 사람이면서 내 소유인 것 하나뿐이다.
            bool isLocal = !e.IsBot && e.Identity != null && e.Identity.IsMine;

            if (isLocal)
                BindMinimapCamera(tf);

            SpawnArrow(tf, isLocal ? localPlayerColor : (e.IsBot ? botColor : remotePlayerColor));
        }
    }

    //태그가 없거나 컴포넌트가 빠져 있으면 조용히 넘어간다.
    //예전엔 한 줄로 이어 붙여서 태그가 없으면 여기서 NullReference가 났다
    private void BindMinimapCamera(Transform target)
    {
        GameObject cam = GameObject.FindGameObjectWithTag(GameTags.MinimapCamera);
        if (cam == null)
            return;

        TopDownCameraFollow follow = cam.GetComponent<TopDownCameraFollow>();
        if (follow != null)
            follow.Target = target;
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

        // MinimapArrow 컴포넌트 설정
        MinimapArrow minimapArrow = arrow.GetComponent<MinimapArrow>();
        if (minimapArrow != null)
        {
            minimapArrow.Target = target;
            minimapArrow.Offset = arrowOffset;
            minimapArrow.SetColor(color);
        }
        else
            Debug.LogWarning("[MinimapArrowManager] arrowPrefab에 MinimapArrow 컴포넌트가 없습니다.");

        arrows[target] = arrow;
    }

    // ─────────────────────────────────────────────────────────
    // 파괴된 대상 정리
    // ─────────────────────────────────────────────────────────
    private void CleanupDestroyedTargets()
    {
        List<Transform> toRemove = null;

        foreach (var kvp in arrows)
        {
            // Transform이 null이면 오브젝트가 파괴된 것
            if (kvp.Key == null)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);
                if (toRemove == null)
                    toRemove = new List<Transform>();
                toRemove.Add(kvp.Key);
            }
        }

        if (toRemove != null)
        {
            foreach (var key in toRemove)
                arrows.Remove(key);
        }
    }

}
