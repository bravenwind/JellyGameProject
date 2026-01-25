using System.Collections;
using System.Collections.Generic;
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

    // 변수 선언부 추가
    private Queue<float> cameraSizeQueue = new Queue<float>();
    private bool isCameraScaling = false;

    // 🔥 수정됨: 큐에 크기 변화량(+ 또는 -)만 등록합니다.
    public void ScaleIncreased()
    {
        cameraSizeQueue.Enqueue(DataManager.Instance.scaleChangedPlusSize);
        if (!isCameraScaling) StartCoroutine(ProcessCameraQueue());
    }

    public void ScaleDecreased()
    {
        cameraSizeQueue.Enqueue(-DataManager.Instance.scaleChangedPlusSize);
        if (!isCameraScaling) StartCoroutine(ProcessCameraQueue());
    }

    // 큐 처리 코루틴
    private IEnumerator ProcessCameraQueue()
    {
        isCameraScaling = true;

        while (cameraSizeQueue.Count > 0)
        {
            float deltaSize = cameraSizeQueue.Dequeue();

            // 코루틴이 시작되는 바로 '이 시점'의 카메라 크기를 기준으로 목표치 설정
            float startSize = Camera.main.orthographicSize;
            float targetSize = startSize + deltaSize;

            // 하나의 카메라 연출이 끝날 때까지 대기
            yield return StartCoroutine(OnScaleChanged_Co(startSize, targetSize, DataManager.Instance.scaleChangedDuration));
        }

        isCameraScaling = false;
    }

    // 🔥 수정됨: 시작 크기(startSize)와 목표 크기(targetSize)를 매개변수로 직접 받음
    IEnumerator OnScaleChanged_Co(float startSize, float targetSize, float duration)
    {
        float t = 0f;

        while (t <= duration)
        {
            t += Time.unscaledDeltaTime;
            Camera.main.orthographicSize = Mathf.Lerp(startSize, targetSize, t / duration);
            yield return null;
        }

        Camera.main.orthographicSize = targetSize; // 정확한 값으로 안착
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