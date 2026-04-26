using UnityEngine;

/// <summary>
/// AI 봇의 주변 엔티티 탐지를 담당.
/// AIPlayerMovement에서 탐지 로직을 분리.
/// </summary>
public class AIDetector : MonoBehaviour
{
    [Header("탐지")]
    public float detectRadius = 15f;
    public float baseAgentRadius = 0.5f;

    /// <summary>
    /// 나보다 큰 위협을 탐지.
    /// 표면 간의 거리(Edge Distance)를 기준으로 탐지.
    /// </summary>
    public Transform FindThreat()
    {
        float myScale = transform.localScale.x;
        return FindEntityByScaleComparison(myScale, biggerThanMe: true);
    }

    /// <summary>
    /// 나보다 작은 먹잇감(플레이어 또는 다른 봇)을 탐지.
    /// </summary>
    public Transform FindPrey()
    {
        float myScale = transform.localScale.x;
        return FindEntityByScaleComparison(myScale, biggerThanMe: false);
    }

    /// <summary>
    /// 추적할 대상을 결정. (작은 플레이어 1순위, 젤리 2순위)
    /// </summary>
    public Transform FindTargetToChase()
    {
        Transform prey = FindPrey();
        if (prey != null) return prey;
        return FindNearestJelly();
    }

    /// <summary>탐지 반경 내 가장 가까운 젤리</summary>
    public Transform FindNearestJelly()
    {
        Transform nearest = null;
        float minDist = detectRadius;

        foreach (var j in EntityRegistry.Jellies)
        {
            if (j == null) continue;
            float d = Vector3.Distance(transform.position, j.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = j.transform;
            }
        }
        return nearest;
    }

    /// <summary>
    /// 스케일 비교를 통해 플레이어/봇 엔티티를 탐색.
    /// FindThreat과 FindPrey의 중복 로직을 통합.
    /// </summary>
    private Transform FindEntityByScaleComparison(float myScale, bool biggerThanMe)
    {
        Transform closest = null;
        float minEdgeDist = float.MaxValue;

        foreach (var p in EntityRegistry.Players)
        {
            if (p == null) continue;
            var selfBot = p.GetComponentInParent<AIPlayerMovement>();
            if (selfBot != null && selfBot.gameObject == gameObject) continue;

            float otherScale = p.ScaleValue;
            if (!IsScaleMatch(myScale, otherScale, biggerThanMe)) continue;

            float edgeDist = CalcEdgeDistance(p.transform.position, myScale, otherScale);
            if (edgeDist < detectRadius && edgeDist < minEdgeDist)
            {
                minEdgeDist = edgeDist;
                closest = p.transform;
            }
        }

        foreach (var b in EntityRegistry.Bots)
        {
            if (b == null || b.gameObject == gameObject) continue;

            float otherScale = b.transform.localScale.x;
            if (!IsScaleMatch(myScale, otherScale, biggerThanMe)) continue;

            float edgeDist = CalcEdgeDistance(b.transform.position, myScale, otherScale);
            if (edgeDist < detectRadius && edgeDist < minEdgeDist)
            {
                minEdgeDist = edgeDist;
                closest = b.transform;
            }
        }

        return closest;
    }

    private bool IsScaleMatch(float myScale, float otherScale, bool biggerThanMe)
    {
        return biggerThanMe ? otherScale > myScale : otherScale < myScale;
    }

    private float CalcEdgeDistance(Vector3 otherPos, float myScale, float otherScale)
    {
        float distToCenter = Vector3.Distance(transform.position, otherPos);
        return distToCenter - (myScale * baseAgentRadius) - (otherScale * baseAgentRadius);
    }
}
