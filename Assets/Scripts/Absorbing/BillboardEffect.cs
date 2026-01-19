using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))] // 스프라이트 렌더러 필수
public class BillboardEffect : MonoBehaviour
{
    [Header("Billboard Settings")]
    [SerializeField] private bool freezeXZAxis = false; // Y축 고정 여부

    [Header("Animation Settings")]
    [SerializeField] private float lifeTime = 2.0f;     // 효과가 지속되는 총 시간
    [SerializeField] private float maxScale = 1.0f;     // 가장 커졌을 때의 크기

    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private float timer = 0f;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color;

        // 시작할 때 크기와 투명도를 0으로 초기화
        transform.localScale = Vector3.zero;
        SetAlpha(0f);
    }

    void Update()
    {
        // 1. 애니메이션 처리 (생성 -> 커짐 -> 작아짐 -> 파괴)
        HandleAnimation();
    }

    void LateUpdate()
    {
        // 2. 빌보드 처리 (카메라 바라보기)
        HandleBillboard();
    }

    // --- 기능 구현부 ---

    void HandleAnimation()
    {
        timer += Time.deltaTime;

        // 진행률 (0 ~ 1 사이의 값)
        float progress = timer / lifeTime;

        if (progress >= 1.0f)
        {
            Destroy(gameObject); // 수명이 다하면 파괴
            return;
        }

        // Mathf.Sin(progress * PI)는 0에서 시작해 1까지 부드럽게 올라갔다가 0으로 내려옵니다.
        // 그래프 모양: ∩ (산 모양)
        float curveValue = Mathf.Sin(progress * Mathf.PI);

        // 크기 적용 (부드럽게 커졌다 작아짐)
        transform.localScale = Vector3.one * (maxScale * curveValue);

        // 알파값 적용 (점점 불투명해졌다 투명해짐)
        SetAlpha(curveValue);
    }

    void HandleBillboard()
    {
        if (Camera.main == null) return;

        if (freezeXZAxis)
        {
            Vector3 targetPosition = transform.position + Camera.main.transform.rotation * Vector3.forward;
            Vector3 lookAtPos = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
            transform.LookAt(lookAtPos);
        }
        else
        {
            transform.rotation = Camera.main.transform.rotation;
        }
    }

    void SetAlpha(float alpha)
    {
        Color color = originalColor;
        color.a = alpha;
        spriteRenderer.color = color;
    }
}