using UnityEngine;

public class MinimapArrow : MonoBehaviour
{
    [Header("추적 대상 (플레이어)")]
    public Transform target;

    [Header("위치 오프셋 (높이 조절)")]
    [Tooltip("플레이어 머리 위쪽, 미니맵 카메라 높이에 맞게 Y값을 조정하세요.")]
    public Vector3 offset = new Vector3(0f, 2f, 0f);

    [Header("회전 동기화 설정")]
    [Tooltip("체크하면 플레이어 전방방향 화살표의 Y축(좌우 방향)만 회전합니다.")]
    public bool syncOnlyYRotation = true;

    // ─────────────────────────────────────────────────────────
    // 색상 설정 (미니맵 매니저에서 초기화 시 호출)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 화살표의 색상을 설정합니다.
    /// SpriteRenderer → .color 직접 설정 (스프라이트 tint)
    /// MeshRenderer  → 인스턴스 머티리얼의 color 설정
    /// ※ 스프라이트는 반드시 흰색 이미지여야 원하는 색상이 정확히 나옵니다.
    ///   빨간 스프라이트에 초록을 곱하면 검정이 되기 때문입니다.
    /// </summary>
    public void SetColor(Color color)
    {
        // SpriteRenderer는 .color 프로퍼티로 직접 tint
        SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in spriteRenderers)
        {
            sr.color = color;
        }

        // MeshRenderer 등 일반 Renderer는 인스턴스 머티리얼 색상 변경
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r is SpriteRenderer) continue; // 위에서 이미 처리
            r.material.color = color;          // 인스턴스 머티리얼 자동 생성
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // 1. 위치 동기화 (목표 위치 + 오프셋)
        transform.position = target.position + offset;

        // 2. 회전 동기화
        if (syncOnlyYRotation)
        {
            Vector3 targetEuler = target.eulerAngles;
            transform.rotation = Quaternion.Euler(transform.eulerAngles.x, targetEuler.y, transform.eulerAngles.z);
        }
        else
        {
            transform.rotation = target.rotation;
        }
    }
}
