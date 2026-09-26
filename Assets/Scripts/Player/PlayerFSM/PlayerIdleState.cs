using UnityEngine;
using UnityEngine.Playables;

// ==========================================
// 1. Idle(대기) 상태 클래스
// ==========================================
public class PlayerIdleState : PlayerBaseState
{
    public PlayerIdleState(PlayerMovement player) : base(player) { }

    public override void Enter()
    {
        // [N1] 매 Idle 진입마다 찍히던 로그 제거 — 전환 로그는 ChangeState(에디터 전용) 한 곳으로 통일.
        player.InputDir = Vector3.zero;

        // ★ 세로 속도를 0으로 되돌리지 않는다
        //   예전엔 여기서 VerticalVelocity = 0을 했다. Idle이 착지 뒤에만 오던 때는 무해했지만,
        //   공중 대쉬·공격이 끝나면 공중에서 Idle로 들어온다. 그때 0으로 만들면 떨어지던 캐릭터가
        //   한순간 멈췄다가 다시 떨어진다. 땅에 있을 때의 정리는 ApplyGravity가 이미 한다(-2로 고정).
    }

    public override void Update()
    {
        player.ApplyGravity();
        player.Controller.Move(new Vector3(0, player.VerticalVelocity, 0) * Time.deltaTime);

        if (player.TryStartAction())
            return;

        if (player.IsMoveInputActive())
        {
            player.ChangeState(player.MoveState);
            return;
        }
    }

    public override void Exit() { }
}
