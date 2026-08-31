using UnityEngine;

public class MinimapArrow : MonoBehaviour
{
    [Header("추적 대상 (플레이어)")]
    [SerializeField] private Transform target;
    public Transform Target { get { return target; } set { target = value; } }

    // ★ 높이의 출처는 이 프리팹 하나다
    //   예전엔 public 세터가 있어서 MinimapArrowManager가 씬의 값으로 덮어썼다.
    //   그래서 프리팹에 적힌 68.32가 죽고 씬의 60이 이겼는데, 프리팹만 보는 사람은
    //   어느 쪽이 진짜인지 알 수가 없었다. 밖에서 바꿀 일이 없으니 세터를 없앤다.
    [Header("위치 오프셋 (높이 조절)")]
    [Tooltip("플레이어 머리 위쪽, 미니맵 카메라 높이에 맞게 Y값을 조정하세요.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, 0f);

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
    /// 화살표의 색을 정한다. 스폰 직후 한 번만 불린다.
    ///
    /// ※ 스프라이트는 반드시 흰색이어야 원하는 색이 정확히 나온다.
    ///   빨간 스프라이트에 초록을 곱하면 검정이 되기 때문이다.
    ///
    /// ★ 젤리를 먹어 몸 색이 바뀌어도 따라 바꾸지 않는다 — <b>의도한 것이다.</b>
    ///   미니맵 화살표는 "저게 나인가 남인가"를 가르는 용도라 초록/빨강 둘이면 충분하고,
    ///   몸 색을 따라가면 그 구분이 무너진다.
    ///   몸 색을 보여주는 건 화면 밖 삼각형(OffScreenPlayerIndicator) 쪽 일이고
    ///   그쪽은 매 프레임 갱신한다. 두 표시가 답하는 질문이 서로 다르다.
    ///
    /// ★ 예전엔 루프가 둘이었다 — 하나는 헛돌고 하나는 위험했다
    ///   ① SpriteRenderer 배열을 foreach로 돌았지만 프리팹의 스프라이트는 하나뿐이다.
    ///   ② 이어서 Renderer 배열을 돌며 MeshRenderer를 칠했는데, 그 배열이 돌려주는 건
    ///      방금 그 SpriteRenderer 하나뿐이고 바로 다음 줄 `if (r is SpriteRenderer) continue;`가
    ///      그걸 걸러낸다. <b>즉 그 루프의 본문은 한 번도 실행되지 않았다.</b>
    ///      실행되지 않은 게 다행이기도 했다 — 본문이 r.material.color였는데 .material은
    ///      접근하는 순간 머티리얼 사본을 만들고 아무도 Destroy하지 않는다. 배칭도 깨진다.
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
