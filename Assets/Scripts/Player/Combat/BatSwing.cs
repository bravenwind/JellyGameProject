using UnityEngine;
using JellyNet;

/// <summary>
/// 배트를 한 번 휘두를 때 <b>누가 휘두르든 똑같이</b> 일어나는 일들.
///
/// ★ 예전엔 두 벌이었다
///   PlayerAttackState.Enter(사람)와 AIPlayerMovement.TryAttack(봇)이
///   애니 트리거 → 배트 회전 → 원격 알림 → 디버그 표시를 각자 적어두고 있었다.
///   네 줄짜리 나열이라 눈에 잘 안 띄는데, 하나를 바꾸면 다른 쪽도 찾아 고쳐야 했다.
///
/// ★ 여기 없는 것
///   쿨다운을 어디에 적는지, 명중을 언제 찾는지는 각자 다르다.
///     사람 → FSM 상태(PlayerAttackState)가 Update에서 찾는다
///     봇   → 코루틴(AttackSwingRoutine)이 찾는다
///   '휘두르는 순간의 연출'만 공통이므로 그것만 담는다.
/// </summary>
public static class BatSwing
{
    /// <summary>휘두르는 연출을 시작한다. 명중 판정은 부르는 쪽이 따로 돌린다.</summary>
    /// <param name="owner">휘두르는 쪽의 루트 Transform</param>
    /// <param name="anim">캐릭터 애니메이터 (없으면 건너뜀)</param>
    /// <param name="visual">배트 회전·원격 알림 창구 (없으면 건너뜀)</param>
    /// <param name="scale">판정에 쓰는 크기. 디버그 표시의 사거리에 쓴다</param>
    public static void Play(Transform owner, Animator anim, LanPlayerVisual visual, float scale)
    {
        DataManager dm = DataManager.Instance;

        if (dm == null)
            return;

        if (anim != null)
            anim.SetTrigger(AnimParams.Attack);

        //배트 회전은 LanPlayerVisual이 돌린다 — 사람·봇·원격 화면이 같은 코드다
        if (visual != null)
        {
            visual.PlayBatSwing();
            visual.SendTrigger(LanPlayerVisual.ANIM_ATTACK);
        }

        BatDebugVisualizer.NotifySwing(
            owner,
            dm.BatRange * scale,
            dm.BatArcAngle * 0.5f,
            dm.BatSwingDuration);
    }
}
