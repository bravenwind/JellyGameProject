using UnityEngine;

/// <summary>
/// 프레임 수에 좌우되지 않는 지수 감쇠 보간 계수.
///
/// ★ 왜 Lerp(a, b, speed * Time.deltaTime) 이 틀린가
///   Lerp의 t는 '거리'가 아니라 '이번에 얼마나 좁힐지'의 비율이고, 그 결과를 다시
///   자기 자신에 넣어 매 프레임 반복한다. 그래서 남은 오차가 (1 - speed·dt)^n 으로
///   <b>프레임 수가 지수에 들어간다.</b>
///     60fps: 0.833^60 ≈ 1.8e-5      30fps: 0.667^30 ≈ 5.2e-6   ← 낮은 fps가 더 빨리 수렴
///   게다가 speed·dt 가 1을 넘으면 Lerp가 클램프되어 그 프레임에 목표로 순간이동한다.
///   로딩 직후처럼 프레임이 튀는 순간 회전이 뚝 끊기는 것이 이 때문이다.
///
///   1 - e^(-k·dt) 를 쓰면 T초 뒤 남는 오차가 e^(-k·T) 로 <b>쪼개는 방식과 무관</b>해진다.
///   지수의 덧셈이 곱셈이 되기 때문이다:  e^(-k·T) = e^(-k·dt₁) × e^(-k·dt₂) × …
///
///   이 프로젝트는 색·위치 보간(LanPlayerState, NetTransform, NetKnockback)에서
///   이미 같은 공식을 쓰고 있었다. 회전만 옛 방식으로 남아 있어 맞춘다.
/// </summary>
public static class SmoothDamping
{
    /// <summary>
    /// 지수 감쇠 보간의 t. speed가 클수록 빨리 목표에 붙는다.
    /// Lerp/Slerp의 세 번째 인자로 그대로 넣으면 된다.
    /// </summary>
    public static float Factor(float speed, float deltaTime)
    {
        return 1f - Mathf.Exp(-speed * deltaTime);
    }

    /// <summary>진행 방향으로 부드럽게 감아 도는 회전. 사람·봇·젤리가 같은 식을 쓴다.</summary>
    public static Quaternion RotateTowards(Quaternion current, Vector3 forward, float speed, float deltaTime)
    {
        return Quaternion.Slerp(current, Quaternion.LookRotation(forward), Factor(speed, deltaTime));
    }
}
