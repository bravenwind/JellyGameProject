using UnityEngine;

public class OverlapTriggerUI : MonoBehaviour
{
    [Header("설정")]
    public GameObject uiObject;       // 켜고 끌 UI 객체
    public float checkRadius = 3.0f;  // 감지 반경
    public LayerMask targetLayer;     // 감지할 레이어 (예: Player)
    public float checkInterval = 0.2f; // 체크 주기 (초)

    [Header("젤리 소환")]
    public GameObject spawnJelly;
    public Transform jellySpawnTransform;
    public Rotator rotator;

    private bool isDetected = false;

    void Start()
    {
        if (uiObject != null) uiObject.SetActive(false);

        // 성능을 위해 일정 간격으로만 체크 실행
        InvokeRepeating(nameof(CheckNearby), 0f, checkInterval);
    }

    private void Update()
    {
        if (isDetected)
        {
            if (Input.GetKeyDown(KeyCode.G))
            {
                rotator.Rotate360(SpawnJelly);
            }
        }
    }

    void CheckNearby()
    {
        // 1. 반경 내에 지정된 레이어의 콜라이더가 있는지 확인
        Collider[] colliders = Physics.OverlapSphere(transform.position, checkRadius, targetLayer);

        // 2. 검색된 콜라이더가 1개 이상이면 범위 안에 있는 것으로 간주
        bool currentlyDetected = colliders.Length > 0;

        // 3. 상태가 바뀔 때만 SetActive 호출 (매번 호출 방지)
        if (currentlyDetected != isDetected)
        {
            isDetected = currentlyDetected;
            if (uiObject != null)
            {
                uiObject.SetActive(isDetected);
            }
        }
    }

    void SpawnJelly()
    {
        Instantiate(spawnJelly, jellySpawnTransform.position, Quaternion.identity);
    }

    // 에디터 뷰에서 감지 범위를 시각적으로 표시
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, checkRadius);
    }
}