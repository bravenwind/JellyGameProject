using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class MainCamera_Action : MonoBehaviour
{
    public Transform target;
    public CameraPosAndRot debug;

    public float followSpeed = 10f;

    [Header("Offset (Local Space)")]
    public Vector3 offset = new Vector3(0f, 10f, -10f);

    [Header("Camera Rotation")]
    public float pitch = 0.0f;   // X
    public float yaw = -45f;    // Y

    public float currentSize;
    public float targetSize;

    [Header("Base Settings")]
    [Tooltip("레벨 1일 때의 기본 카메라 크기 (기존 리셋값인 6.1 기준)")]
    public float baseOrthographicSize = 6.1f;

    // 기존 Lerp 대신 SmoothDamp 사용 권장
    private Vector3 currentVelocity; // SmoothDamp용 참조 변수
    public float smoothTime = 0.1f;  // followSpeed 대신 사용 (작을수록 빠름)

    Rigidbody targetRb;

    void Awake()
    {
        targetRb = target.GetComponent<Rigidbody>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        Quaternion camRot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 followPos =
        targetRb != null ? targetRb.position : target.position;

        Vector3 desiredPos = followPos + camRot * offset;

        // Lerp 대신 SmoothDamp 사용
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPos,
            ref currentVelocity,
            smoothTime
        );
    }

    public void ScaleIncreased()
    {
        currentSize = Camera.main.orthographicSize;
        float targetSize = currentSize + DataManager.Instance.scaleChangedPlusSize;
        StartCoroutine(OnScaleChanged_Co(targetSize, DataManager.Instance.scaleChangedDuration));
    }

    public void ScaleDecreased()
    {
        currentSize = Camera.main.orthographicSize;
        float targetSize = currentSize - DataManager.Instance.scaleChangedPlusSize;
        StartCoroutine(OnScaleChanged_Co(targetSize, DataManager.Instance.scaleChangedDuration));
    }

    // 🔥 [추가] 특정 레벨에 맞는 카메라 크기로 바로 변경하는 함수
    public void ChangeCameraSizeToLevel(int targetLevel)
    {
        currentSize = Camera.main.orthographicSize;

        // 목표 사이즈 계산: 기본 사이즈 + (레벨 차이 * 단계당 증가 사이즈)
        float targetSize = baseOrthographicSize + (targetLevel - 1) * DataManager.Instance.scaleChangedPlusSize;

        StartCoroutine(OnScaleChanged_Co(targetSize, DataManager.Instance.scaleChangedDuration));
    }

    IEnumerator OnScaleChanged_Co(float targetSize, float duration)
    {
        float t = 0f;

        while (t <= duration)
        {
            // 🔥 버그 수정: Time.deltaTime을 루프 안에서 매 프레임 새로 받아오도록 수정
            t += Time.unscaledDeltaTime;
            Camera.main.orthographicSize = Mathf.Lerp(currentSize, targetSize, t / duration);
            yield return null;
        }

        Camera.main.orthographicSize = targetSize; // 정확한 값으로 안착
    }

    public void GameFailSizeChange()
    {
        currentSize = Camera.main.orthographicSize;
        float targetSize = currentSize * 0.5f;
        Debug.Log(currentSize + " " + currentSize * 0.5f);
        StartCoroutine(OnScaleChanged_Co(targetSize, 0.5f));
    }
}