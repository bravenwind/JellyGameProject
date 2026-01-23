using UnityEngine;
using System.Collections; // IEnumerator 사용을 위해 필수

public class OverlapTriggerUI : MonoBehaviour
{
    [Header("설정")]
    public GameObject uiObject;
    public float checkRadius = 3.0f;
    public LayerMask targetLayer;

    // checkInterval은 이제 필요 없거나, 감지 쿨타임 정도로 쓸 수 있습니다.
    // 여기서는 연속 실행을 위해 제거하거나 무시해도 됩니다.

    [Header("젤리 소환")]
    public GameObject spawnJelly;
    public Transform jellySpawnTransform;
    public Rotator rotator; // (코드에 없어서 주석 처리)

    private bool isProcessing = false; // 현재 기계가 동작 중인지 확인하는 플래그

    void Start()
    {
        if (uiObject != null) uiObject.SetActive(false);

        // 🔴 InvokeRepeating 제거 (코루틴으로 제어하기 위함)
    }

    void Update()
    {
        if (!isProcessing)
        {
            StartCoroutine(SpawnSequence());
        }
        // 동작 중이 아닐 때만 감지 시도
        //if (!isProcessing)
        //{
        //    CheckNearbyAndProcess();
        //}
    }

    void CheckNearbyAndProcess()
    {
        // 1. 범위 감지
        Collider[] colliders = Physics.OverlapSphere(transform.position, checkRadius, targetLayer);
        bool currentlyDetected = colliders.Length > 0;

        if (currentlyDetected)
        {
            // 감지되면 시퀀스 시작 (소리 -> 대기 -> 소환)
            StartCoroutine(SpawnSequence());
        }
    }

    // 🔴 [핵심 로직] 소리 재생 후 대기하고 소환하는 코루틴
    IEnumerator SpawnSequence()
    {
        isProcessing = true; // 작업 시작 (중복 실행 방지)

        // 1. 소리 재생하고 길이 받아오기
        float waitTime = PlayFXAudio.Instance.PlayMachineSound();

        // 2. 소리 길이만큼 대기 (소리가 끝날 때까지 멈춤)
        yield return StartCoroutine(rotator.RotateRoutine(waitTime, SpawnJelly));

        isProcessing = false; // 작업 종료 (이제 다시 감지 가능)

        StartCoroutine(SpawnSequence());
    }

    void SpawnJelly()
    {
        Instantiate(spawnJelly, jellySpawnTransform.position, Quaternion.identity);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}