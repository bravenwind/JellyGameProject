using UnityEngine;
using TMPro; // TextMeshPro를 사용하기 위해 필요 (일반 Text라면 UnityEngine.UI)

public class JellyTextFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform targetObj;
    public Vector3 worldOffset = new Vector3(0, 2.5f, 0);

    [Header("Movement (Slippery Feel)")]
    [Tooltip("값이 클수록 더 미끄러지듯 늦게 따라옵니다. (0이면 즉시 이동)")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.15f;

    [Header("Jelly Animation")]
    [Tooltip("애니메이션 속도")]
    public float wobbleSpeed = 5f;
    [Tooltip("찌그러지는 정도 (0 ~ 0.5 권장)")]
    public float wobbleAmount = 0.2f;

    private Camera mainCamera;
    private Vector3 currentVelocity; // SmoothDamp 계산용 변수
    private TextMeshProUGUI tmpText; // 일반 Text 사용 시 Text로 변경

    void Start()
    {
        mainCamera = Camera.main;
        tmpText = GetComponent<TextMeshProUGUI>();

        if (targetObj == null) Debug.LogWarning("타겟 오브젝트가 없습니다!");
    }

    void LateUpdate()
    {
        if (targetObj == null) return;

        // 1. 목표 위치 계산 (3D -> 2D 변환)
        Vector3 targetWorldPos = targetObj.position + worldOffset;
        Vector3 targetScreenPos = mainCamera.WorldToScreenPoint(targetWorldPos);

        // 카메라 뒤에 있는지 확인
        if (targetScreenPos.z < 0)
        {
            if (tmpText != null) tmpText.enabled = false;
        }
        else
        {
            if (tmpText != null) tmpText.enabled = true;
            targetScreenPos.z = 0; // UI 평면이므로 Z는 0

            // 2. [핵심] 미끄러지는 이동 (SmoothDamp)
            // 바로 대입하지 않고, 목표 지점까지 부드럽게 미끄러지며 도달하게 합니다.
            transform.position = Vector3.SmoothDamp(transform.position, targetScreenPos, ref currentVelocity, smoothTime);

            // 3. [핵심] 젤리처럼 꿀렁거리는 스케일 (Squash & Stretch)
            ApplyJellyEffect();
        }
    }

    void ApplyJellyEffect()
    {
        // Sin 함수를 이용해 -1 ~ 1 사이 값을 반복
        float sineWave = Mathf.Sin(Time.time * wobbleSpeed);

        // X축이 늘어나면 Y축은 줄어들고, 반대로 X가 줄면 Y가 늘어나게 하여 부피감을 유지
        // 1.0f를 기준으로 wobbleAmount 만큼 더하고 뺍니다.
        float scaleX = 1.0f + (sineWave * wobbleAmount);
        float scaleY = 1.0f - (sineWave * wobbleAmount);

        transform.localScale = new Vector3(scaleX, scaleY, 1f);
    }
}