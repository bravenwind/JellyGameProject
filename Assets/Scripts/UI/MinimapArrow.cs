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
