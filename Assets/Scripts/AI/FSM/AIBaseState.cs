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

        var collapse = TileCollapseManager.Instance;
        if (collapse != null
            && collapse.IsPathDangerous(ai.CachedPath.corners, ai.CachedPath.corners.Length))
            return false;

        ai.Agent.SetPath(ai.CachedPath);
        return true;
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
