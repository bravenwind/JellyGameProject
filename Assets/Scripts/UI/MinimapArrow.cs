using UnityEngine;

public class MinimapArrow : MonoBehaviour
{
    [Header("추적 대상 (플레이어)")]
    [SerializeField] private Transform target;
    public Transform Target { get { return target; } set { target = value; } }

    [Header("위치 오프셋 (높이 조절)")]
    [Tooltip("플레이어 머리 위쪽, 미니맵 카메라 높이에 맞게 Y값을 조정하세요.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, 0f);
    public Vector3 Offset { get { return offset; } set { offset = value; } }

    [Header("회전 동기화 설정")]
    [Tooltip("체크하면 플레이어 전방방향 화살표의 Y축(좌우 방향)만 회전합니다.")]
    [SerializeField] private bool syncOnlyYRotation = true;

    // ─────────────────────────────────────────────────────────
    // 색상 설정 (미니맵 매니저에서 초기화 시 호출)
    // ─────────────────────────────────────────────────────────

    [Tooltip("색을 입힐 화살표 스프라이트. 비워두면 자식에서 찾는다.")]
    [SerializeField] private SpriteRenderer icon;

    //프리팹의 SpriteRenderer는 루트가 아니라 자식(MiniMapIcon)에 있다 —
    //GetComponent로는 못 찾으므로 InChildren이어야 한다.
    private void Awake()
    {
        if (icon == null)
            icon = GetComponentInChildren<SpriteRenderer>(true);
    }

    /// <summary>
    /// 화살표의 색을 정한다. 스폰 직후 한 번 불린다.
    ///
    /// ※ 스프라이트는 반드시 흰색이어야 원하는 색이 정확히 나온다.
    ///   빨간 스프라이트에 초록을 곱하면 검정이 되기 때문이다.
    ///
    /// ★ 예전엔 루프가 둘이었다 — 하나는 헛돌고 하나는 위험했다
    ///   ① GetComponentsInChildren&lt;SpriteRenderer&gt;를 foreach로 돌았다.
    ///      프리팹의 스프라이트는 <b>하나뿐</b>이라(루트 + MiniMapIcon 자식 구조)
    ///      배열을 만들어 한 바퀴 도는 것이 전부였다.
    ///
    ///   ② 이어서 GetComponentsInChildren&lt;Renderer&gt;를 돌며 MeshRenderer를 칠했는데,
    ///      그 배열이 돌려주는 건 방금 그 SpriteRenderer 하나뿐이고 바로 다음 줄
    ///      `if (r is SpriteRenderer) continue;`가 그걸 걸러낸다.
    ///      <b>즉 그 루프의 본문은 한 번도 실행되지 않았다.</b>
    ///
    ///      실행되지 않은 게 다행이기도 했다 — 본문이 `r.material.color`였는데,
    ///      .material은 접근하는 순간 머티리얼 사본을 만들고 아무도 Destroy하지 않는다.
    ///      배칭도 깨진다. FallingTile에서 같은 문제를 MaterialPropertyBlock으로
    ///      이미 고친 적이 있다.
    /// </summary>
    public void SetColor(Color color)
    {
        if (icon != null)
            icon.color = color;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // 1. 위치 동기화 (목표 위치 + 오프셋)
        transform.position = target.position + offset;

        // 2. 회전 동기화
        if (syncOnlyYRotation)
        {
            Vector3 targetEuler = target.eulerAngles;
            transform.rotation = Quaternion.Euler(transform.eulerAngles.x, targetEuler.y, transform.eulerAngles.z);
        }
        else
            transform.rotation = target.rotation;
    }
}
