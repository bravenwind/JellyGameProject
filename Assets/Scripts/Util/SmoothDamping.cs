using UnityEngine;

public static class SmoothDamping
{
    public static float Factor(float speed, float deltaTime)
    {
        return 1f - Mathf.Exp(-speed * deltaTime);
    }

    public static Quaternion RotateTowards(Quaternion current, Vector3 forward, float speed, float deltaTime)
    {
        return Quaternion.Slerp(current, Quaternion.LookRotation(forward), Factor(speed, deltaTime));
    }
}
