using UnityEngine;
using Photon.Pun;

public class AIDetector : MonoBehaviour
{
    [Header("탐지")]
    public float detectRadius = 15f;
    public float baseAgentRadius = 0.5f;

    private AIPlayerMovement _owner;

    private void Awake()
    {
        _owner = GetComponent<AIPlayerMovement>();
    }

    public Transform FindThreat()
    {
        float myScale = _owner != null ? _owner.GetMyAuthorityScale() : transform.localScale.x;
        return FindEntityByScaleComparison(myScale, biggerThanMe: true);
    }

    public Transform FindPrey()
    {
        float myScale = _owner != null ? _owner.GetMyAuthorityScale() : transform.localScale.x;
        return FindEntityByScaleComparison(myScale, biggerThanMe: false);
    }

    public Transform FindTargetToChase()
    {
        Transform prey = FindPrey();
        if (prey != null) return prey;
        return FindNearestJelly();
    }

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

            float otherScale = b.GetMyAuthorityScale();
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
