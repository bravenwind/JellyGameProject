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
    [Tooltip("흐름 주기 변화 시간")]
    public float changeDirectionInterval;

    [Header("수명 설정")]
    [Tooltip("초콜릿에 빠진 오브젝트가 자동 비활성화되기까지의 시간 (초). 0이면 비활성화 안 함")]
    public float floatingLifetime = 5f;

    [Header("디버그")]
    public bool debugLogTriggers = true;

    // 현재 흐르는 방향 (코루틴에서 실시간으로 변경됨)
    private Vector3 _currentFlowDirection;

    // OnTriggerEnter에서 이미 물리 설정 완료된 Rigidbody 캐싱 (Stay에서 중복 GetComponent 방지)
    private readonly HashSet<Rigidbody> _processedBodies = new HashSet<Rigidbody>();

    private struct FloatData
    {
        public float phase;
        public float speedMul;
        public float forceMul;
        public Vector2 flowOffset;
    }
    private readonly Dictionary<Rigidbody, FloatData> _floatData = new Dictionary<Rigidbody, FloatData>();

    private const float PurgeInterval = 5f;
    private float _lastPurgeTime;

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
        if (Time.time - _lastPurgeTime >= PurgeInterval)
        {
            PurgeDestroyedEntries();
            _lastPurgeTime = Time.time;
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        // OnTriggerEnter 누락 안전장치: 아직 미처리된 오브젝트만 체크
        if (!_processedBodies.Contains(rb) && (rb.useGravity || rb.isKinematic))
        {
            bool isEdible = other.CompareTag("Edible");
            int bgLayer = LayerMask.NameToLayer("BackGroundObject");
            bool isBackgroundObject = (bgLayer >= 0) &&
                (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);

            if (isEdible || isBackgroundObject)
            {
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.linearDamping = chocolateViscosity;
                rb.angularDamping = chocolateViscosity;
                _processedBodies.Add(rb);
            }
        }

        // 부력 (수면 아래일 때만)
        if (rb.position.y < transform.position.y)
            rb.AddForce(Vector3.up * buoyancyForce, ForceMode.Acceleration);

        // 오브젝트별 고유 출렁임
        if (!_floatData.TryGetValue(rb, out FloatData fd))
        {
            fd = CreateFloatData(rb);
            _floatData[rb] = fd;
        }

        float waveY = Mathf.Sin(Time.time * waveSpeed * fd.speedMul + fd.phase);
        rb.AddForce(new Vector3(
            (_currentFlowDirection.x + fd.flowOffset.x) * flowForce,
            waveY * waveForce * fd.forceMul,
            (_currentFlowDirection.z + fd.flowOffset.y) * flowForce
        ), ForceMode.Acceleration);
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
            _processedBodies.Add(rb);

            if (!isAI && floatingLifetime > 0f)
                StartCoroutine(DeactivateAfterDelay(rb.gameObject, floatingLifetime));
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

        netPlayer.SyncEliminated();
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

    private static FloatData CreateFloatData(Rigidbody rb)
    {
        int id = rb.GetInstanceID();
        return new FloatData
        {
            phase = (id * 2.3f) % (Mathf.PI * 2f),
            speedMul = 0.7f + ((id * 7.9f) % 1000f) / 1000f * 0.6f,
            forceMul = 1.2f + ((id * 3.1f) % 1000f) / 1000f * 0.8f,
            flowOffset = new Vector2(
                ((id * 5.7f) % 1000f) / 1000f * 0.6f - 0.3f,
                ((id * 11.3f) % 1000f) / 1000f * 0.6f - 0.3f)
        };
    }

    private IEnumerator DeactivateAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null)
        {
            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                _processedBodies.Remove(rb);
                _floatData.Remove(rb);
            }
            obj.SetActive(false);
        }
    }

    private void PurgeDestroyedEntries()
    {
        _processedBodies.RemoveWhere(rb => rb == null);

        List<Rigidbody> staleKeys = null;
        foreach (var kvp in _floatData)
        {
            if (kvp.Key == null)
            {
                staleKeys ??= new List<Rigidbody>();
                staleKeys.Add(kvp.Key);
            }
        }
        if (staleKeys != null)
            foreach (var key in staleKeys)
                _floatData.Remove(key);
    }

    private void OnDisable()
    {
        _processedBodies.Clear();
        _floatData.Clear();
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        _processedBodies.Remove(rb);
        _floatData.Remove(rb);

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