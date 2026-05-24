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
        // 게임 시작 시 주기적으로 방향을 바꾸는 코루틴 실행
        StartCoroutine(ChangeDirectionRoutine());
    }

    // 지정된 시간마다 X, Z 방향을 랜덤으로 바꾸는 함수
    private IEnumerator ChangeDirectionRoutine()
    {
        while (true)
        {
            // X, Z 방향을 -1 ~ 1 사이의 랜덤 값으로 설정
            float randomX = Random.Range(-1f, 1f);
            float randomZ = Random.Range(-1f, 1f);

            // Y는 Update에서 실시간 계산하므로 일단 0으로 둠
            _currentFlowDirection = new Vector3(randomX, 0, randomZ).normalized;

            // changeDirectionInterval(예: 3초) 만큼 대기 후 다시 방향 변경
            yield return new WaitForSeconds(changeDirectionInterval);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;

        if (rb != null)
        {
            // 1. 기본 부력 적용 (물체가 초콜릿 표면보다 아래에 있으면 위로 밀어올림)
            if (other.transform.position.y < transform.position.y)
            {
                rb.AddForce(Vector3.up * buoyancyForce, ForceMode.Acceleration);
            }

            // 2. 시간에 따른 Y축 출렁임 계산 (-1 ~ 1 사이를 부드럽게 진동)
            float waveY = Mathf.Sin(Time.time * waveSpeed);

            // 3. 최종 흐름 힘 계산 (랜덤 X, Z + 출렁이는 Y)
            Vector3 finalFlowDirection = _currentFlowDirection;
            finalFlowDirection.y = waveY; // Y축에 -1 ~ 1 값 대입

            // X, Z는 flowForce만큼, Y는 waveForce만큼의 크기로 힘을 가함
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
        // NetworkPlayer(실제 플레이어) → 게임 오버 + 둥둥 떠다니기
        NetworkPlayerSync netPlayer = other.GetComponentInParent<NetworkPlayerSync>();
        if (netPlayer != null)
        {
            if (debugLogTriggers) Debug.Log($"[Chocolate] ENTER NetworkPlayer: {other.name}");
            HandlePlayerEnterChocolate(netPlayer);
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

        if (debugLogTriggers)
        {
            string category = isEdible ? "Edible" : isAI ? "AI" : isBackgroundObject ? "BG" : "기타(무시)";
            Debug.Log($"[Chocolate] ENTER [{category}]: collider={other.name}(layer={LayerMask.LayerToName(other.gameObject.layer)}) rb={rb.name}(layer={LayerMask.LayerToName(rb.gameObject.layer)} kin={rb.isKinematic} gravity={rb.useGravity})");
        }

        if (isEdible || isAI || isBackgroundObject)
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

    private void HandlePlayerEnterChocolate(NetworkPlayerSync netPlayer)
    {
        if (netPlayer.playerController != null)
            netPlayer.playerController.enabled = false;

        if (netPlayer.photonView.IsMine)
            GameModeManager.Instance?.GameOver();

        CharacterController cc = netPlayer.GetComponent<CharacterController>();
        if (cc != null && netPlayer.GetComponent<Rigidbody>() == null)
        {
            float radius = cc.radius;
            float height = cc.height;
            Vector3 center = cc.center;
            cc.enabled = false;

            CapsuleCollider capsule = netPlayer.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = center;

            Rigidbody rb = netPlayer.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.linearDamping = chocolateViscosity;
            rb.angularDamping = chocolateViscosity;
        }
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

        // OnTriggerEnter에서 꺼놓은 AI 컴포넌트 복구
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
            rb.useGravity = false;
            navMeshAgent.enabled = true;
        }
        if (wanderingAI != null) wanderingAI.enabled = true;
        if (aiWaypointPatrol != null) aiWaypointPatrol.enabled = true;
    }
}