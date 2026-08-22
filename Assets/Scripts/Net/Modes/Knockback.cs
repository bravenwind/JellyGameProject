using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 밀려나는 속도가 시간에 따라 어떻게 줄어드는가. 사람·봇 공용 공식.
    ///
    /// ★ 왜 따로 뺐나
    ///   같은 감쇠가 두 곳에 복사돼 있었다.
    ///     PlayerKnockbackState : KNOCKBACK_DURATION = 0.4f, Lerp(v, 0, t)
    ///     AIPlayerMovement     : duration = 0.4f,           Lerp(dir*force, 0, t)
    ///   상수도 곡선도 같은데 파일이 달라서, 밀치기 손맛을 바꾸려면 두 곳을 같이 고쳐야 했고
    ///   한쪽만 고치면 사람과 봇이 다르게 밀려난다.
    ///
    /// ★ 무엇을 합치고 무엇을 남겼나
    ///   '속도를 어떻게 줄일까'는 같지만 '그 속도로 어떻게 움직일까'는 다르다.
    ///     사람 → CharacterController.Move (벽에 막힌다)
    ///     봇   → transform.position += (NavMeshAgent를 끄고 직접 몬다)
    ///   그래서 속도 계산만 여기로 모으고 이동은 각자 한다.
    ///
    ///   NetKnockback(씬 소품용)은 일부러 남겨뒀다. 그쪽은 지속 시간이 정해진 게 아니라
    ///   지수 감쇠로 서서히 멎는 방식이라 곡선 자체가 다르다.
    /// </summary>
    public static class Knockback
    {
        /// <summary>밀려나는 시간(초). 이 시간이 지나면 속도가 0이 된다.</summary>
        public const float DURATION = 0.4f;

        /// <summary>시작 속도. 방향은 정규화하고 수평 성분만 남긴다.</summary>
        public static Vector3 StartVelocity(Vector3 direction, float force)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            return direction.normalized * force;
        }

        /// <summary>경과 시간에 해당하는 지금 속도. elapsed가 DURATION을 넘으면 0.</summary>
        public static Vector3 VelocityAt(Vector3 startVelocity, float elapsed)
        {
            float t = Mathf.Clamp01(elapsed / DURATION);
            return Vector3.Lerp(startVelocity, Vector3.zero, t);
        }

        /// <summary>아직 밀려나는 중인가.</summary>
        public static bool IsActive(float elapsed)
        {
            return elapsed < DURATION;
        }
    }
}
