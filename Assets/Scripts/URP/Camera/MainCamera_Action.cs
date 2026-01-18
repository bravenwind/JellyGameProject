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

    public void ScaleChanged()
    {
        if (DataManager.Instance.playerCurrentLevel <= DataManager.Instance.maxLevel)
        {
            currentSize = Camera.main.orthographicSize;
            StartCoroutine(OnScaleChanged_Co(DataManager.Instance.scaleChangedDuration));
        }
    }

    IEnumerator OnScaleChanged_Co(float duration)
    {
        float t = 0f;
        float delta = Time.deltaTime;

        float targetSize = currentSize + DataManager.Instance.scaleChangedPlusSize;
        while (t <= duration)
        {
            t += delta;
            Camera.main.orthographicSize = Mathf.Lerp(currentSize, targetSize, t / duration);
            yield return null;
        }
    }
}
