// ============================================================
// AIBotController.cs
// ============================================================
// 역할: 플레이어와 동일한 방식으로 행동하는 AI 봇
//   - NavMeshAgent로 자동 이동
//   - 젤리 먹으면 성장 (PlayerScaleController.SetScaleLevel 사용)
//   - 더 큰 상대 접근 시 즉시 도망, 더 작으면 추격
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;
using ExitGames.Client.Photon;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PhotonView))]
public class AIBotController : MonoBehaviourPun, IPunObservable
{
    [Header("컴포넌트 연결 (AIBot 프리팹 기준)")]
    public PlayerScaleController scaleController;
    public Renderer jellyRenderer;
    public Animator animator;

    [Header("이동")]
    public float baseMoveSpeed = 5f;
    public float detectRadius = 15f;    // 플레이어/봇 탐지 반경

    // ── 봇 전용 내부 상태 (DataManager 사용 X) ──
    private int   _scaleLevel     = 1;
    private int   _absorbedCount  = 0;
    private int   _score          = 0;
    private float _moveSpeed;

    public int CurrentScaleLevel => _scaleLevel;
    public int Score             => _score;
    public string NameTag        { get; private set; }

    // ── 네트워크 보간용 ──
    private Vector3    _netPos;
    private Quaternion _netRot;
    private int        _netScaleLevel = 1;

    private NavMeshAgent _agent;

    // ── 중복 흡수 방지 ──
    private readonly System.Collections.Generic.HashSet<int> _absorbed = new();

    // ================================================================
    // 초기화
    // ================================================================
    private void Awake() => _agent = GetComponent<NavMeshAgent>();

    private void Start()
    {
        NameTag      = $"Bot_{Random.Range(100, 999)}";
        _moveSpeed   = baseMoveSpeed;
        _agent.speed          = _moveSpeed;
        _agent.stoppingDistance = 0.3f;
        _agent.angularSpeed   = 360f;

        if (PhotonNetwork.IsMasterClient)
            StartCoroutine(WaitForNavMeshThenStart());
        else
            _agent.enabled = false;     // 원격 클라이언트: 위치만 보간
    }

    // ── NavMesh에 올라갈 때까지 대기 후 AI 시작 ──
    private IEnumerator WaitForNavMeshThenStart()
    {
        float elapsed = 0f;
        while (!_agent.isOnNavMesh && elapsed < 5f)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 100f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            elapsed += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        if (_agent.isOnNavMesh)
        {
            Debug.Log($"[AIBot] {NameTag} 시작");
            StartCoroutine(AILoop());
        }
        else
            Debug.LogWarning($"[AIBot] {NameTag} NavMesh 실패 → 비활성화");
    }

    // ================================================================
    // AI 루프 (MasterClient 전용)
    // ================================================================
    private IEnumerator AILoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.3f);
            if (!_agent.isOnNavMesh) continue;

            // 1. 주변 젤리 흡수 시도
            TryAbsorbNearbyJellies();

            // 2. 이미 이동 중이면 목적지 유지 (멈추지 않음)
            if (_agent.pathPending) continue;
            if (_agent.hasPath && _agent.remainingDistance > 1f) continue;

            // 3. 도달했거나 경로 없음 → 다음 목적지 결정
            PickNextDestination();
        }
    }

    // ================================================================
    // 다음 목적지 결정: 도망 > 젤리 탐색 > 배회
    // ================================================================
    private void PickNextDestination()
    {
        _agent.isStopped = false;

        // ── 위협(나보다 큰 상대) → 즉시 도망 ──
        Transform threat = FindThreat();
        if (threat != null)
        {
            Vector3 fleeDir = (transform.position - threat.position).normalized;
            foreach (float d in new[] { 20f, 15f, 10f, 5f })
            {
                if (NavMesh.SamplePosition(transform.position + fleeDir * d, out NavMeshHit fh, d, NavMesh.AllAreas))
                {
                    _agent.SetDestination(fh.position);
                    _agent.speed = _moveSpeed * 1.5f;
                    return;
                }
            }
        }

        // ── 가장 가까운 젤리로 이동 ──
        Transform jelly = FindNearestJelly();
        if (jelly != null)
        {
            _agent.SetDestination(jelly.position);
            _agent.speed = _moveSpeed;
            return;
        }

        // ── 젤리 없으면 배회 ──
        for (int i = 0; i < 15; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(5f, 20f);
            Vector3 dir = transform.position + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            if (NavMesh.SamplePosition(dir, out NavMeshHit wh, 20f, NavMesh.AllAreas))
            {
                if (Vector3.Distance(transform.position, wh.position) >= 1f)
                {
                    _agent.SetDestination(wh.position);
                    _agent.speed = _moveSpeed * 0.8f;
                    return;
                }
            }
        }
    }

    // ================================================================
    // 탐지
    // ================================================================

    private Transform FindNearestJelly()
    {
        JellyObject[] all = FindObjectsByType<JellyObject>(FindObjectsSortMode.None);
        Transform nearest  = null;
        float minDist      = float.MaxValue;
        foreach (var j in all)
        {
            if (j == null) continue;
            float d = Vector3.Distance(transform.position, j.transform.position);
            if (d < minDist) { minDist = d; nearest = j.transform; }
        }
        return nearest;
    }

    private Transform FindThreat()
    {
        Collider[] cols = Physics.OverlapSphere(transform.position, detectRadius);
        foreach (var col in cols)
        {
            NetworkPlayerSync p = col.GetComponentInParent<NetworkPlayerSync>();
            if (p != null && p.ScaleLevel > _scaleLevel) return p.transform;

            AIBotController b = col.GetComponentInParent<AIBotController>();
            if (b != null && b != this && b.CurrentScaleLevel > _scaleLevel) return b.transform;
        }
        return null;
    }

    // ================================================================
    // 젤리 흡수 (OverlapSphere + OnTriggerEnter 양쪽에서 체크)
    // ================================================================

    private void TryAbsorbNearbyJellies()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        Collider[] cols = Physics.OverlapSphere(transform.position, 2f);
        foreach (var col in cols)
        {
            if (!col.CompareTag("Edible")) continue;
            if (col.GetComponentInParent<AIBotController>() != null) continue;
            if (col.GetComponentInParent<NetworkPlayerSync>()  != null) continue;

            JellyObject jelly = col.GetComponentInParent<JellyObject>();
            if (jelly == null) continue;

            PhotonView pv = jelly.GetComponent<PhotonView>();
            if (pv == null || !_absorbed.Add(pv.ViewID)) continue;  // 중복 방지

            AbsorbJelly(jelly.jellyType, jelly.gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (other.CompareTag("Edible"))
        {
            JellyObject jelly = other.GetComponentInParent<JellyObject>();
            if (jelly != null)
            {
                PhotonView pv = jelly.GetComponent<PhotonView>();
                if (pv != null && _absorbed.Add(pv.ViewID))
                    AbsorbJelly(jelly.jellyType, jelly.gameObject);
            }
        }

        // 더 작은 플레이어 흡수
        NetworkPlayerSync player = other.GetComponentInParent<NetworkPlayerSync>();
        if (player != null && player.ScaleLevel < _scaleLevel)
            player.photonView.RPC("RPC_GetAbsorbed", RpcTarget.All, photonView.ViewID);
    }

    // ── 젤리 흡수 처리 ──
    private void AbsorbJelly(JellyColorType type, GameObject jellyObj)
    {
        _score         += 100;
        _absorbedCount ++;

        // 색상 변화 (Renderer 직접 제어)
        ApplyColor(type);

        // 레벨업 체크 (DataManager의 scaleLevelUpExp 기준 재사용)
        if (_absorbedCount >= DataManager.Instance.scaleLevelUpExp)
        {
            _absorbedCount = 0;
            LevelUp();
        }

        // 젤리 삭제 요청
        NetworkJellyManager.Instance?.RequestDestroyJelly(jellyObj);
        SyncProperties();
    }

    // ================================================================
    // 레벨업 (PlayerScaleController.SetScaleLevel 사용)
    // ================================================================
    private void LevelUp()
    {
        if (_scaleLevel >= DataManager.Instance.maxScaleLevel) return;
        _scaleLevel++;
        _moveSpeed *= 1.1f;
        _agent.speed = _moveSpeed;

        // PlayerScaleController가 붙어 있으면 스케일 애니메이션 위임
        if (scaleController != null)
            scaleController.SetScaleLevel(_scaleLevel);
        else
        {
            int idx = Mathf.Clamp(_scaleLevel - 1, 0, DataManager.Instance.scalePerLevel.Length - 1);
            transform.localScale = Vector3.one * DataManager.Instance.scalePerLevel[idx];
        }

        Debug.Log($"[AIBot] {NameTag} 레벨업 → {_scaleLevel}");
    }

    // ================================================================
    // 색상 (RYB 기반, Renderer 직접 제어)
    // ================================================================
    private RYBColor _botRYB = RYBColor.white;

    private void ApplyColor(JellyColorType type)
    {
        if (jellyRenderer == null) return;

        if (type == JellyColorType.White)
        {
            _botRYB = RYBColor.white;
        }
        else
        {
            RYBColor effect = DataManager.Instance.GetJellyRYBEffect(type);
            _botRYB = _botRYB.Add(effect);
        }

        Color visual = _botRYB.ToRGB();

        jellyRenderer.material.SetColor("_BaseColor_01", visual * 0.85f);
        jellyRenderer.material.SetColor("_FresnelColor",  visual);
        jellyRenderer.material.SetColor("_BaseColor_02",  Color.Lerp(visual, Color.white, 0.6f));
    }

    // ================================================================
    // 네트워크 동기화
    // ================================================================
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(_scaleLevel);
            stream.SendNext(_score);
        }
        else
        {
            _netPos        = (Vector3)   stream.ReceiveNext();
            _netRot        = (Quaternion)stream.ReceiveNext();
            _netScaleLevel = (int)       stream.ReceiveNext();
            _score         = (int)       stream.ReceiveNext();

            transform.position = Vector3.Lerp(transform.position, _netPos, 0.1f);
            transform.rotation = Quaternion.Lerp(transform.rotation, _netRot, 0.1f);

            if (DataManager.Instance != null)
            {
                int idx = Mathf.Clamp(_netScaleLevel - 1, 0, DataManager.Instance.scalePerLevel.Length - 1);
                transform.localScale = Vector3.one * DataManager.Instance.scalePerLevel[idx];
            }
        }
    }

    private void SyncProperties()
    {
        var props = new ExitGames.Client.Photon.Hashtable
        {
            { $"Bot_{photonView.ViewID}_Score", _score },
            { $"Bot_{photonView.ViewID}_Level", _scaleLevel },
            { $"Bot_{photonView.ViewID}_Name",  NameTag }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
