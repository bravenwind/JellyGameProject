// ============================================================
// AIBaseState.cs
// ============================================================
// PlayerBaseState와 동일한 패턴의 AI 상태 기본 클래스.
// 모든 AI 상태(Wander, Chase, Flee, PushSurvive)가 이 클래스를 상속.
//
// 상태들이 똑같이 반복하던 두 가지를 여기로 올렸다.
//   · 경로를 깔기 전에 위험 구간을 지나는지 확인 (TrySetSafePath)
//   · 경로는 있는데 안 움직이는 끼임 감지 (HandleStuck)
// 예전엔 앞의 것이 세 파일에 글자까지 똑같이 복사돼 있었고, 그러면서
// AIWanderState만 그 검사를 빠뜨려 배회 경로가 무너지는 발판을 관통했다.
// ============================================================

using UnityEngine;
using UnityEngine.AI;

public abstract class AIBaseState
{
    protected AIPlayerMovement ai;

    public AIBaseState(AIPlayerMovement ai)
    {
        this.ai = ai;
    }

    /// <summary>상태 진입 시 1회 호출</summary>
    public abstract void Enter();

    /// <summary>매 프레임 호출 (AIPlayerMovement.Update에서)</summary>
    public abstract void Update();

    /// <summary>상태 이탈 시 1회 호출</summary>
    public abstract void Exit();

    // ─────────────────────────────────────────────────────────
    // 공용: 안전 경로 설정
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 목적지까지 경로를 계산하고, <b>완주 가능하며 위험 구간을 지나지 않을 때만</b> 적용한다.
    ///
    /// ★ CalculatePath는 PathPartial(가는 데까지만)에도 true를 준다.
    ///   그래서 status를 따로 봐야 한다 — 안 보면 봇이 벽 앞이나 무너진
    ///   구멍 가장자리까지 걸어가서 멈춘다.
    ///
    /// ★ 위험 검사는 corner(꺾이는 점)만 본다.
    ///   corner 사이 직선이 위험 타일을 스치는 건 못 잡는다. 완벽한 필터가 아니라
    ///   "대놓고 붕괴 구역을 경유하는 경로"를 걸러내는 1차 방어다.
    /// </summary>
    /// <returns>경로를 실제로 적용했으면 true. false면 호출부가 대안을 시도해야 한다.</returns>
    protected bool TrySetSafePath(Vector3 destination)
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return false;

        //CachedPath는 봇 하나가 평생 재사용하는 객체라 직전 질의의 corner가 남아 있다.
        //비우지 않으면 계산이 실패했을 때 아래 위험 검사가 '지난 경로'를 보고 판단한다
        ai.CachedPath.ClearCorners();

        if (!ai.Agent.CalculatePath(destination, ai.CachedPath)
            || ai.CachedPath.status != NavMeshPathStatus.PathComplete)
            return false;

        //CachedPath.corners는 읽을 때마다 배열을 새로 만드는 프로퍼티다. 한 번만 읽는다.
        var collapse = TileCollapseManager.Instance;
        if (collapse != null && collapse.IsPathDangerous(ai.CachedPath.corners))
            return false;

        ai.Agent.SetPath(ai.CachedPath);
        return true;
    }

    /// <summary>
    /// <b>이미 위험한 칸에 서 있을 때</b> 쓰는 경로 설정.
    /// 지금 선 칸은 못 본 척하고, <paramref name="allowDangerousCrossing"/>이 false면
    /// 그 밖의 위험 칸을 지나는 경로는 거절한다(호출부가 다른 후보를 보게).
    ///
    /// ★ 왜 '지금 선 칸만' 예외인가
    ///   이미 꺼지는 발판 위에 있으면 TrySetSafePath는 항상 false다 —
    ///   corners[0]이 곧 내가 선 자리이기 때문이다. 그래서 도주 경로는 아예 검사를
    ///   건너뛰고 있었는데, 그건 <b>남의 빨간 칸까지 가로질러도 된다</b>는 뜻이 됐다.
    ///   막지 말아야 할 것은 '내가 선 칸에서 나가는 것' 하나뿐이다.
    /// </summary>
    protected bool TrySetEscapePath(Vector3 destination, bool allowDangerousCrossing)
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return false;

        ai.CachedPath.ClearCorners();

        if (!ai.Agent.CalculatePath(destination, ai.CachedPath)
            || ai.CachedPath.status != NavMeshPathStatus.PathComplete)
            return false;

        // ★ 감수 모드에서도 <b>이미 무너지는 중인 칸</b>은 지나지 않는다
        //   예전엔 allowDangerousCrossing이 검사를 통째로 껐다. 그래서 흔들리는
        //   빨간 타일을 태연히 밟고 지나가는 봇이 나왔다.
        //   닳은 칸은 지나가도 밟는 순간 무너지지 않지만, 붕괴가 시작된 칸은
        //   collapseDelay 뒤에 바닥이 사라진다 — 그 위를 지나다 만나면 떨어진다.
        //   그래서 감수해도 되는 것(닳음)과 감수하면 안 되는 것(붕괴중)을 나눈다.
        var collapse = TileCollapseManager.Instance;
        if (collapse != null)
        {
            bool blocked = allowDangerousCrossing
                ? collapse.IsPathOverCollapsing(ai.CachedPath.corners, ai.transform.position)
                : collapse.IsPathDangerousIgnoringStart(ai.CachedPath.corners, ai.transform.position);

            if (blocked)
                return false;
        }

        ai.Agent.SetPath(ai.CachedPath);
        return true;
    }

    /// <summary>
    /// 완주 경로가 없어도 갈 수 있는 데까지 간다. 물러설 곳의 맨 끝이다.
    ///
    /// ★ 제자리가 언제나 최악이다
    ///   예전엔 도주 분기가 `CalculatePath && status == PathComplete` 하나로 끝나서,
    ///   주변이 무너져 NavMesh가 잘리면 아무 일도 안 일어났다 — 봇은 꺼지는 발판
    ///   위에 선 채로 판단을 포기했다. 서 있으면 확실히 죽고 움직이면 죽을 수도
    ///   있을 뿐이다.
    /// </summary>
    protected bool TrySetPartialPath(Vector3 destination)
    {
        if (!ai.Agent.enabled || !ai.Agent.isOnNavMesh)
            return false;

        // ★ SetDestination 을 그냥 부르지 않는다 — 그러면 검사할 기회가 없다
        //   먼저 계산해 보고, 붕괴중인 칸을 지나지 않는 부분 경로면 그걸 쓴다.
        //   그마저 없으면 그때는 SetDestination 이다. 여기까지 왔다는 건
        //   '어디로도 깨끗하게 갈 수 없다'는 뜻이고, 그럴 땐 제자리가 확실한 죽음이라
        //   붕괴중인 칸을 지나서라도 움직이는 편이 낫다.
        ai.CachedPath.ClearCorners();

        if (ai.Agent.CalculatePath(destination, ai.CachedPath)
            && ai.CachedPath.status != NavMeshPathStatus.PathInvalid)
        {
            var collapse = TileCollapseManager.Instance;
            if (collapse == null
                || !collapse.IsPathOverCollapsing(ai.CachedPath.corners, ai.transform.position))
            {
                ai.Agent.SetPath(ai.CachedPath);
                return true;
            }
        }

        return ai.Agent.SetDestination(destination);
    }

    // ─────────────────────────────────────────────────────────
    // 공용: 끼임 감지
    // ─────────────────────────────────────────────────────────

    private float stuckTimer;

    protected const float STUCK_SECONDS = 1.0f;

    //제곱 비교라 sqrt를 안 탄다. 0.1f 속도의 제곱이 0.01f
    private const float STUCK_SPEED_SQR = 0.01f;

    /// <summary>
    /// "경로는 있는데 실제로는 안 움직인다"가 일정 시간 이어지면 경로를 버린다.
    /// 벽 모서리에 비비거나 서로 밀며 교착된 상태를 푸는 용도.
    /// </summary>
    /// <returns>이번 프레임에 경로를 버렸으면 true — 호출부는 즉시 return해야 한다.</returns>
    protected bool HandleStuck()
    {
        if (ai.Agent.hasPath && ai.Agent.velocity.sqrMagnitude < STUCK_SPEED_SQR)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= STUCK_SECONDS)
            {
                stuckTimer = 0f;
                ai.Agent.ResetPath();   //다음 갱신 때 새 길을 찾도록 유도
                return true;
            }
            return false;
        }

        stuckTimer = 0f;
        return false;
    }

    /// <summary>상태에 들어올 때 끼임 누적을 초기화한다.</summary>
    protected void ResetStuck()
    {
        stuckTimer = 0f;
    }
}
