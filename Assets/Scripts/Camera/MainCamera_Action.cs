using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainCamera_Action : MonoBehaviour
{
    [SerializeField] private Transform target;
    public Transform Target { get { return target; } }

    [Header("Offset (Local Space)")]
    public Vector3 offset = new Vector3(0f, 10f, -10f);

    [Header("Camera Rotation")]
    [SerializeField] private float pitch = 0.0f;   // X
    [SerializeField] private float yaw = -45f;    // Y

    // 기존 Lerp 대신 SmoothDamp 사용 권장
    private Vector3 currentVelocity; // SmoothDamp용 참조 변수
    [SerializeField] private float smoothTime = 0.1f;  // 작을수록 빠르게 따라붙는다

    Rigidbody targetRb;

    void Awake()
    {
        if (target != null)
            targetRb = target.GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        GameState.OnCameraScaleIncreased += ScaleIncreased;
    }

    private void OnDisable()
    {
        GameState.OnCameraScaleIncreased -= ScaleIncreased;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        targetRb = target != null ? target.GetComponent<Rigidbody>() : null;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

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
        cameraSizeQueue.Enqueue(DataManager.Instance.ScaleChangedPlusSize);
        if (!isCameraScaling)
            StartCoroutine(ProcessCameraQueue(DataManager.Instance.CameraZoomDuration));
    }

    // 큐 처리 코루틴
    private IEnumerator ProcessCameraQueue(float changeDuration)
    {
        isCameraScaling = true;

        while (cameraSizeQueue.Count > 0)
        {
            float deltaSize = cameraSizeQueue.Dequeue();

            // 코루틴이 시작되는 바로 '이 시점'의 카메라 크기를 기준으로 목표치 설정
            float startSize = Camera.main.orthographicSize;
            float targetSize = startSize + deltaSize;

            // 하나의 카메라 연출이 끝날 때까지 대기
            yield return StartCoroutine(OnScaleChanged_Co(startSize, targetSize, changeDuration));
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

}