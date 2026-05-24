using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class ChocolateFluid : MonoBehaviour
{
    [Header("기본 설정")]
    [Tooltip("기본 부력 (가라앉지 않게 밀어올리는 힘)")]
    public float buoyancyForce = 15f;

    [Tooltip("초콜릿의 점성 (높을수록 끈적함)")]
    public float chocolateViscosity = 3f;

    [Header("랜덤 움직임 설정")]
    [Tooltip("X, Z 방향이 바뀌는 주기 (초)")]
    public float changeDirectionInterval = 3f;

    [Tooltip("수평(X, Z)으로 흐르는 힘")]
    public float flowForce = 5f;

    [Tooltip("Y축 출렁임 속도 (파도의 빠르기)")]
    public float waveSpeed = 2f;

    [Tooltip("Y축 출렁임 강도 (위아래로 밀어주는 힘)")]
    public float waveForce = 3f;

    [Header("디버그")]
    [Tooltip("OnTriggerEnter로 진입하는 모든 콜라이더 정보를 콘솔에 출력")]
    public bool debugLogTriggers = true;

    // 현재 흐르는 방향 (코루틴에서 실시간으로 변경됨)
    private Vector3 _currentFlowDirection;

    private void Start()
    {
        StartCoroutine(ChangeDirectionRoutine());
    }

    private IEnumerator ChangeDirectionRoutine()
    {
        while (true)
        {
            float randomX = Random.Range(-1f, 1f);
            float randomZ = Random.Range(-1f, 1f);

            _currentFlowDirection = new Vector3(randomX, 0, randomZ).normalized;

            yield return new WaitForSeconds(changeDirectionInterval);
        }
    }

    // FixedUpdate 주기와 싱크를 맞추기 위해 유체 물리 연산은 OnTriggerStay에서 확실히 보정
    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;

        if (rb != null)
        {
            // [해결책 1] 유체 내부에서 다른 스크립트가 중력을 켜는 것을 방지하기 위해 Stay에서 매번 꺼줌
            if (other.CompareTag("Edible") || other.CompareTag("BackGroundObject") || rb.GetComponent<AIPlayerMovement>() != null || rb.GetComponent<WanderingAI>() != null)
            {
                rb.useGravity = false;
                rb.isKinematic = false;
                rb.linearDamping = chocolateViscosity;
                rb.angularDamping = chocolateViscosity;
            }

            // [해결책 2] 물리 엔진이 이 오브젝트를 Sleep 상태로 만드는 것을 방지 (부력이 안 먹히는 현상 해결)
            if (rb.IsSleeping())
            {
                rb.WakeUp();
            }

            // 1. 기본 부력 적용 (물체가 초콜릿 표면보다 아래에 있으면 위로 밀어올림)
            if (other.transform.position.y < transform.position.y)
            {
                // 조금 더 확실한 상승을 위해 속도 비례 댐핑을 뚫고 밀어올리도록 힘 보정
                rb.AddForce(Vector3.up * buoyancyForce, ForceMode.Acceleration);
            }

            // 2. 시간에 따른 Y축 출렁임 계산
            float waveY = Mathf.Sin(Time.time * waveSpeed);

            // 3. 최종 흐름 힘 계산
            Vector3 finalFlowDirection = _currentFlowDirection;
            finalFlowDirection.y = waveY;

            Vector3 appliedForce = new Vector3(
                finalFlowDirection.x * flowForce,
                finalFlowDirection.y * waveForce,
                finalFlowDirection.z * flowForce
            );

            // 4. 물체에 힘 가하기
            rb.AddForce(appliedForce, ForceMode.Acceleration);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // NetworkPlayer(실제 플레이어) 발견 시
        NetworkPlayerSync netPlayer = other.GetComponentInParent<NetworkPlayerSync>();
        if (netPlayer != null)
        {
            if (debugLogTriggers) Debug.Log($"[Chocolate] ENTER NetworkPlayer: {other.name}");

            // [수정] 직접 컴포넌트를 건드리지 않고, 플레이어 내부 탈락 함수를 호출!
            netPlayer.SyncChocolateElimination();
            return;
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
        {
            if (debugLogTriggers) Debug.Log($"[Chocolate] ENTER (rb 없음, 무시): {other.name} layer={LayerMask.LayerToName(other.gameObject.layer)} tag={other.tag}");
            return;
        }

        AIPlayerMovement aiPlayer = rb.GetComponent<AIPlayerMovement>();
        WanderingAI wanderingAI = rb.GetComponent<WanderingAI>();
        AIWaypointPatrol aiWaypointPatrol = rb.GetComponent<AIWaypointPatrol>();
        NavMeshAgent navMeshAgent = rb.GetComponent<NavMeshAgent>();

        bool isAI = aiPlayer != null || wanderingAI != null;
        int bgLayer = LayerMask.NameToLayer("BackGroundObject");
        // collider와 rigidbody 양쪽 모두 체크 (자식 콜라이더가 다른 레이어인 경우 대응)
        bool isBackgroundObject = (bgLayer >= 0) &&
            (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);
        bool isEdible = other.CompareTag("Edible");
        bool isCandy = other.CompareTag("Sphere");

        if (debugLogTriggers)
        {
            string category = isEdible ? "Edible" : isAI ? "AI" : isBackgroundObject ? "BG" : "기타(무시)";
            Debug.Log($"[Chocolate] ENTER [{category}]: collider={other.name}(layer={LayerMask.LayerToName(other.gameObject.layer)}) rb={rb.name}(layer={LayerMask.LayerToName(rb.gameObject.layer)} kin={rb.isKinematic} gravity={rb.useGravity})");
        }

        if (isEdible || isAI || isBackgroundObject || isCandy)
        {
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.linearDamping = chocolateViscosity;
            rb.angularDamping = chocolateViscosity;
        }

        if (wanderingAI != null) wanderingAI.enabled = false;
        if (aiWaypointPatrol != null) aiWaypointPatrol.enabled = false;
        if (navMeshAgent != null) navMeshAgent.enabled = false;

        if (aiPlayer != null) aiPlayer.OnEliminated();
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        int bgLayer = LayerMask.NameToLayer("BackGroundObject");
        bool isBackgroundObject = (bgLayer >= 0) &&
            (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);
        if (other.CompareTag("Edible") || isBackgroundObject)
        {
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
            if (isBackgroundObject) rb.useGravity = true;
        }

        AIPlayerMovement aiPlayer = rb.GetComponent<AIPlayerMovement>();
        if (aiPlayer != null && aiPlayer.IsEliminated) return;

        NavMeshAgent navMeshAgent = rb.GetComponent<NavMeshAgent>();
        WanderingAI wanderingAI = rb.GetComponent<WanderingAI>();
        AIWaypointPatrol aiWaypointPatrol = rb.GetComponent<AIWaypointPatrol>();

        if (navMeshAgent != null)
        {
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(rb.transform.position, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                rb.transform.position = hit.position;
            }
            // NavMeshAgent가 켜지면 에이전트 자체 시스템이 이동과 중력을 제어하므로 상황에 맞게 설정
            rb.useGravity = false;
            navMeshAgent.enabled = true;
        }
        if (wanderingAI != null) wanderingAI.enabled = true;
        if (aiWaypointPatrol != null) aiWaypointPatrol.enabled = true;
    }
}