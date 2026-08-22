using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using JellyNet;

public class ChocolateFluid : MonoBehaviour
{
    [Header("기본 설정")]
    [Tooltip("기본 부력 (낙하 가속도를 끊고 밀어올리기 위해 25~30 이상 추천)")]
    public float buoyancyForce = 30f;

    [Tooltip("초콜릿의 점성 (들어오는 순간 속도를 급브레이크 잡기 위해 상향 추천)")]
    public float chocolateViscosity = 5f;

    [Header("독립적인 물결 움직임 설정")]
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
    private int _lastDirectionInterval = -1;

    private void UpdateFlowDirection()
    {
        int interval = GetCurrentInterval();
        if (interval == _lastDirectionInterval) return;
        _lastDirectionInterval = interval;

        var rng = new System.Random(interval);
        float x = (float)(rng.NextDouble() * 2.0 - 1.0);
        float z = (float)(rng.NextDouble() * 2.0 - 1.0);
        _currentFlowDirection = new Vector3(x, 0, z).normalized;
    }

    private int GetCurrentInterval()
    {
        float intervalSec = Mathf.Max(changeDirectionInterval, 0.01f);

        //방향이 양쪽에서 같아야 하므로 각자의 Time.time이 아니라
        //호스트가 맞춰주는 경과 시간을 쓴다
        var flow = LanGameFlow.Instance;
        float t = flow != null && flow.Elapsed >= 0f ? flow.Elapsed : Time.time;

        return (int)(t / intervalSec);
    }

    private void OnTriggerStay(Collider other)
    {
        UpdateFlowDirection();

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
        // ═════════════════════════════════════════════
        //  사람 플레이어 탈락
        // ═════════════════════════════════════════════
        //
        // ★ 이 갈래가 없으면 사람이 아래 Rigidbody 분기로 흘러
        //   '떠다니는 물체' 취급만 받고 죽지 않는다.
        LanPlayerState lanPlayer = other.GetComponentInParent<LanPlayerState>();
        if (lanPlayer != null)
        {
            // 신고는 본인만. 남의 캐릭터가 내 화면에서 스쳤다고 죽이면 안 된다.
            if (lanPlayer.IsMine && !lanPlayer.IsOutOfPlay
                && LanGameFlow.Instance != null)
            {
                LanGameFlow.Instance.ReportSelfEliminated(
                    lanPlayer.EntityId, "초콜릿에 빠졌습니다!");
            }
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

#if UNITY_EDITOR
        // [X5] 트리거 로그는 에디터 전용 — 빌드에서는 플래그가 켜져 있어도 문자열 보간(GC)과
        // Debug.Log(스택트레이스 수집) 비용이 트리거마다 발생하지 않게 한다.
        if (debugLogTriggers)
        {
            string category = isEdible ? "Edible" : isAI ? "AI" : isBackgroundObject ? "BG" : isCandy ? "Candy" : "기타(무시)";
            Debug.Log($"[Chocolate] ENTER [{category}]: {other.name}의 부모 {rb.name} 진입 중!");
        }
#endif

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

        if (aiPlayer != null)
        {
            //탈락 판정 권한은 OnEliminated 안에서 IsDriver로 이미 본다.
            //여기서 또 보면 규칙이 두 곳에 생기고, botId가 없을 때의 폴백까지 달라진다
            aiPlayer.OnEliminated();
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

        AIPlayerMovement aiPlayer = rb.GetComponent<AIPlayerMovement>();

        int bgLayer = LayerMask.NameToLayer("BackGroundObject");
        bool isBackgroundObject = (bgLayer >= 0) && (rb.gameObject.layer == bgLayer || other.gameObject.layer == bgLayer);
        // [MAP-3] AI(봇)도 damping 복원 대상에 포함한다. 예전엔 Edible/배경만 복원해서, 탈락 RPC가
        // 도착하기 전에 부력에 튕겨 초콜릿을 빠져나간 봇의 Rigidbody에 chocolateViscosity(기본 5)가
        // 영구 잔존했다 → 이후 그 봇이 다시 물리 낙하할 때 과감쇠로 궤적이 비정상적으로 느려진다.
        if (other.CompareTag("Edible") || isBackgroundObject || (aiPlayer != null && !aiPlayer.IsEliminated))
        {
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
            if (isBackgroundObject) rb.useGravity = true;
        }

        if (aiPlayer != null && aiPlayer.IsEliminated) return;

        NavMeshAgent navMeshAgent = rb.GetComponent<NavMeshAgent>();
        WanderingAI wanderingAI = rb.GetComponent<WanderingAI>();
        AIWaypointPatrol aiWaypointPatrol = rb.GetComponent<AIWaypointPatrol>();

        // ═════════════════════════════════════════════
        //  [LAN 이식] NavMeshAgent를 되살릴 자격
        // ═════════════════════════════════════════════
        //
        // ★ 문제 두 가지가 여기서 나왔다
        //   ① 원격에서도 agent를 켜버렸다 → 호스트가 보내주는 위치와 agent의
        //      자체 이동이 서로를 밀어 젤리가 떨린다. 원격은 agent가 꺼져 있어야 한다.
        //   ② SamplePosition이 실패했는데도 켰다 → "Failed to create agent because
        //      it is not close enough to the NavMesh"가 프레임마다 쏟아진다.
        //      NavMesh 위로 못 옮겼으면 켜지 않는 게 맞다(다음 Exit 때 다시 시도).
        bool drives = NavDriverOf(rb);

        if (navMeshAgent != null && drives)
        {
            NavMeshHit hit;
            bool onMesh = NavMesh.SamplePosition(
                rb.transform.position, out hit, 10f, NavMesh.AllAreas);

            if (onMesh)
            {
                rb.transform.position = hit.position;
                rb.useGravity = false;
                navMeshAgent.enabled = true;
            }
        }

        // AI 스크립트는 원격에서도 켠다 — 이동은 안 하고 걷는 애니메이션만 맞춘다.
        if (wanderingAI != null) wanderingAI.enabled = true;
        if (aiWaypointPatrol != null) aiWaypointPatrol.enabled = true;
    }

    /// <summary>이 기계가 그 오브젝트의 NavMeshAgent를 굴리는가(= 호스트이거나 오프라인).</summary>
    private static bool NavDriverOf(Rigidbody rb)
    {
        NetIdentity id = rb.GetComponentInParent<NetIdentity>();
        if (id != null) return id.IsSimulatedHere;

        var net = NetManager.Instance;
        return NetManager.Offline || net.IsHost;
    }
}