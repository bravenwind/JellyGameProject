using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class ChocolateFluid : MonoBehaviour
{
    [Header("기본 설정")]
    [Tooltip("기본 부력 (낙하 가속도를 끊고 밀어올리기 위해 25~30 이상 추천)")]
    public float buoyancyForce = 30f;

    [Tooltip("초콜릿의 점성 (들어오는 순간 속도를 급브레이크 잡기 위해 상향 추천)")]
    public float chocolateViscosity = 5f;

    [Header("독립적인 물결 움직임 설정")]
    [Tooltip("흐름 방향이 서서히 바뀌는 속도")]
    public float flowChangeSpeed = 0.5f;
    [Tooltip("수평(X, Z)으로 흐르는 힘")]
    public float flowForce = 5f;
    [Tooltip("Y축 출렁임 속도 (파도의 빠르기)")]
    public float waveSpeed = 2f;
    [Tooltip("Y축 출렁임 강도 (위아래로 밀어주는 힘)")]
    public float waveForce = 3f;

    [Header("디버그")]
    public bool debugLogTriggers = true;

    // 코루틴과 전역 방향 변수(_currentFlowDirection)는 이제 필요 없습니다! 

    private void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        // 실제 유저는 유저 동기화 스크립트(SyncChocolateElimination)에서 별도 제어하므로 제외
        if (rb.GetComponent<NetworkPlayerSync>() != null) return;

        // 대상 필터링 (Edible, AI, 배경 오브젝트, 사탕 등)
        bool isEdible = other.CompareTag("Edible") || rb.gameObject.CompareTag("Edible");
        bool isCandy = other.CompareTag("Sphere") || rb.gameObject.CompareTag("Sphere");
        int bgLayer = LayerMask.NameToLayer("BackGroundObject");
        bool isBackgroundObject = (bgLayer >= 0) && (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);
        bool isAI = rb.GetComponent<AIPlayerMovement>() != null || rb.GetComponent<WanderingAI>() != null;

        if (isEdible || isAI || isBackgroundObject || isCandy)
        {
            if (rb.IsSleeping())
            {
                rb.WakeUp();
            }

            rb.isKinematic = false;
            rb.useGravity = false;
            rb.linearDamping = chocolateViscosity;
            rb.angularDamping = chocolateViscosity;

            // 1. 기본 부력 적용
            if (other.transform.position.y < transform.position.y)
            {
                float antiFallBonus = 0f;
                if (rb.linearVelocity.y < 0)
                {
                    antiFallBonus = Mathf.Abs(rb.linearVelocity.y) * 2f;
                }

                Vector3 upwardForce = Vector3.up * (buoyancyForce + antiFallBonus);
                rb.AddForce(upwardForce, ForceMode.Acceleration);
            }

            // ---------------------------------------------------------
            // [개선된 부분] 물체마다 완전히 독립적인 흐름(Flow) 계산!
            // ---------------------------------------------------------

            // 2. 시간에 따른 Y축 출렁임 계산 (위치값을 더해서 물체마다 다른 타이밍에 출렁이게 함)
            float wavePhase = other.transform.position.x * 0.5f + other.transform.position.z * 0.5f;
            float waveY = Mathf.Sin(Time.time * waveSpeed + wavePhase);

            // 3. 물체 고유의 ID를 이용해 개별적인 X, Z 흐름 방향 계산
            // GetInstanceID()는 오브젝트마다 완전히 다른 숫자를 반환하므로, 이 값을 사인/코사인 함수의 오프셋으로 씁니다.
            float uniqueOffset = rb.gameObject.GetInstanceID();

            float objFlowX = Mathf.Sin(Time.time * flowChangeSpeed + uniqueOffset);
            float objFlowZ = Mathf.Cos(Time.time * (flowChangeSpeed * 0.8f) + uniqueOffset);

            // 4. 물체별 흐름 힘 가하기
            Vector3 appliedForce = new Vector3(
                objFlowX * flowForce,
                waveY * waveForce,
                objFlowZ * flowForce
            );

            rb.AddForce(appliedForce, ForceMode.Acceleration);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        NetworkPlayerSync netPlayer = other.GetComponentInParent<NetworkPlayerSync>();
        if (netPlayer != null)
        {
            if (debugLogTriggers) Debug.Log($"[Chocolate] ENTER NetworkPlayer: {other.name}");
            netPlayer.SyncChocolateElimination();
            return;
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        AIPlayerMovement aiPlayer = rb.GetComponent<AIPlayerMovement>();
        WanderingAI wanderingAI = rb.GetComponent<WanderingAI>();
        AIWaypointPatrol aiWaypointPatrol = rb.GetComponent<AIWaypointPatrol>();
        NavMeshAgent navMeshAgent = rb.GetComponent<NavMeshAgent>();

        bool isEdible = other.CompareTag("Edible") || rb.gameObject.CompareTag("Edible");
        bool isCandy = other.CompareTag("Sphere") || rb.gameObject.CompareTag("Sphere");

        int bgLayer = LayerMask.NameToLayer("BackGroundObject");
        bool isBackgroundObject = (bgLayer >= 0) && (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);

        bool isAI = rb.GetComponent<AIPlayerMovement>() != null || rb.GetComponent<WanderingAI>() != null;

        if (debugLogTriggers)
        {
            string category = isEdible ? "Edible" : isAI ? "AI" : isBackgroundObject ? "BG" : isCandy ? "Candy" : "기타(무시)";
            Debug.Log($"[Chocolate] ENTER [{category}]: {other.name}의 부모 {rb.name} 진입 중!");
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
        bool isBackgroundObject = (bgLayer >= 0) && (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);
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
            rb.useGravity = false;
            navMeshAgent.enabled = true;
        }
        if (wanderingAI != null) wanderingAI.enabled = true;
        if (aiWaypointPatrol != null) aiWaypointPatrol.enabled = true;
    }
}