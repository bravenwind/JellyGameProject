// ============================================================
// AIPlayerMovement.cs
// ============================================================
// 역할: 플레이어와 동일한 CharacterController 기반 이동을
//       키보드 입력 대신 NavMesh 경로로 자동화
//
// AIBot 프리팹 구성:
//   NetworkPlayer와 동일한 컴포넌트 전부 +
//   이 스크립트 추가 + PlayerController 비활성화
//   PlayerAbsorber.isBot = true (Inspector에서 체크)
//   PlayerAbsorbingManager.isBot = true
//   PlayerScaleController.isBot = true
//   PlayerColorVisual.isBot = true
// TODO : FindObjectsByType 대신에 모든 젤리의 리스트를 만들어서 여기서 참조.
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PhotonView))]
public class AIPlayerMovement : MonoBehaviourPun
{
    [Header("이동")]
    public float moveSpeed   = 6f;
    public float rotateSpeed = 10f;
    public float gravity     = -20f;

    [Header("AI")]
    public float detectRadius    = 15f;   // 플레이어/봇 탐지 반경
    public float pathRefreshRate = 0.4f;  // 경로 재계산 주기 (초)

    // ── 내부 ──
    private CharacterController _cc;
    private NavMeshPath         _path;
    private int                 _cornerIndex = 0;
    private float               _verticalVel = 0f;

    // 플레이어 컴포넌트 (스케일 레벨 비교용)
    private PlayerScaleController _scaleCtrl;

    private void Awake()
    {
        _cc       = GetComponent<CharacterController>();
        _path     = new NavMeshPath();
        _scaleCtrl = GetComponent<PlayerScaleController>();
    }

    private void Start()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        StartCoroutine(InitAndRun());
    }

    // ── 스폰 위치를 NavMesh 위로 맞춘 뒤 루프 시작 ──
    private IEnumerator InitAndRun()
    {
        // CharacterController를 잠깐 끄고 Warp (CC가 켜진 상태로 position 바꾸면 무시됨)
        _cc.enabled = false;

        float elapsed = 0f;
        while (elapsed < 5f)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 100f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                Debug.Log($"[AIBot] {name} NavMesh 위치 확정: {hit.position}");
                break;
            }
            elapsed += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        _cc.enabled = true;
        StartCoroutine(PathUpdateLoop());
    }

    // ── 경로 재계산 루프 ──
    private IEnumerator PathUpdateLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(pathRefreshRate);
            Vector3 dest;
            if (TryGetDestination(out dest))
            {
                NavMesh.CalculatePath(transform.position, dest, NavMesh.AllAreas, _path);
                _cornerIndex = 1;
            }
        }
    }

    // ── 목표 위치 결정: 위협 도망 > 젤리 탐색 > 배회 ──
    private bool TryGetDestination(out Vector3 destination)
    {
        int myLevel = _scaleCtrl != null ? GetMyScaleLevel() : 1;

        // 더 큰 상대가 근처에 있으면 → 도망
        Transform threat = FindThreat(myLevel);
        if (threat != null)
        {
            Vector3 fleeDir    = (transform.position - threat.position).normalized;
            Vector3 fleeTarget = transform.position + fleeDir * 20f;
            if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit fh, 20f, NavMesh.AllAreas))
            {
                destination = fh.position;
                return true;
            }
        }

        // 가장 가까운 젤리
        Transform jelly = FindNearestJelly();
        if (jelly != null)
        {
            destination = jelly.position;
            return true;
        }

        // 배회
        return TryGetWanderDestination(out destination);
    }

    private bool TryGetWanderDestination(out Vector3 destination)
    {
        for (int i = 0; i < 10; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(5f, 20f);
            Vector3 candidate = transform.position + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 20f, NavMesh.AllAreas))
            {
                destination = hit.position;
                return true;
            }
        }
        destination = transform.position;
        return false;
    }

    // ── 매 프레임 이동 ──
    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        ApplyGravity();
        FollowPath();

        // 애니메이터
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
            anim.SetBool("IsMoving", _cc.velocity.magnitude > 0.1f);
    }

    private void FollowPath()
    {
        if (_path == null || _path.corners.Length < 2 || _cornerIndex >= _path.corners.Length)
            return;

        Vector3 target = _path.corners[_cornerIndex];
        target.y = transform.position.y;

        Vector3 dir = (target - transform.position).normalized;

        // 이동
        _cc.Move(dir * moveSpeed * Time.deltaTime);

        // 회전
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), rotateSpeed * Time.deltaTime);

        // 코너 도달 체크
        if (Vector3.Distance(transform.position, target) < 0.5f)
            _cornerIndex++;
    }

    private void ApplyGravity()
    {
        if (_cc.isGrounded && _verticalVel < 0f)
            _verticalVel = -2f;

        _verticalVel += gravity * Time.deltaTime;
        _cc.Move(Vector3.up * _verticalVel * Time.deltaTime);
    }

    // ================================================================
    // 탐지 함수
    // ================================================================

    private int GetMyScaleLevel()
    {
        return _scaleCtrl != null ? _scaleCtrl.BotScaleLevel : 1;
    }

    private Transform FindThreat(int myLevel)
    {
        Collider[] cols = Physics.OverlapSphere(transform.position, detectRadius);
        foreach (var col in cols)
        {
            NetworkPlayerSync p = col.GetComponentInParent<NetworkPlayerSync>();
            if (p != null && p.ScaleLevel > myLevel + 1) return p.transform;

            AIPlayerMovement b = col.GetComponentInParent<AIPlayerMovement>();
            if (b != null && b != this)
            {
                float otherScale = b.transform.localScale.x;
                if (otherScale > transform.localScale.x + 0.3f) return b.transform;
            }
        }
        return null;
    }

    private Transform FindNearestJelly()
    {
        JellyObject[] all = FindObjectsByType<JellyObject>(FindObjectsSortMode.None);
        Transform nearest = null;
        float minDist = detectRadius;   // detectRadius 밖은 무시
        foreach (var j in all)
        {
            if (j == null) continue;
            float d = Vector3.Distance(transform.position, j.transform.position);
            if (d < minDist) { minDist = d; nearest = j.transform; }
        }
        return nearest;
    }

    // ── 스케일 변경 후 CharacterController 재정렬 ──
    public void RecenterCC()
    {
        StartCoroutine(RecenterCCRoutine());
    }

    private IEnumerator RecenterCCRoutine()
    {
        _cc.enabled = false;
        yield return null;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            transform.position = hit.position;
        _cc.enabled = true;
        _cornerIndex = _path.corners.Length; // 현재 경로 무효화 → 다음 PathUpdateLoop에서 재계산
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
