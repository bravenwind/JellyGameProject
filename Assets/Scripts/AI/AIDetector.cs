using UnityEngine;

public class AIDetector : MonoBehaviour
{
    [Header("탐지")]
    public float detectRadius = 15f;
    public float baseAgentRadius = 0.5f;

    private AIPlayerMovement _owner;

    private const float ScanCacheDuration = 0.1f;
    private Transform _cachedThreat;
    private Transform _cachedPrey;
    private Transform _cachedJelly;
    private float _lastThreatScan = -1f;
    private float _lastPreyScan = -1f;
    private float _lastJellyScan = -1f;

    private void Awake()
    {
        _owner = GetComponent<AIPlayerMovement>();
    }

    public Transform FindThreat()
    {
        if (Time.time - _lastThreatScan < ScanCacheDuration && _cachedThreat != null)
            return _cachedThreat;
        _lastThreatScan = Time.time;
        float myScale = _owner != null ? _owner.GetMyAuthorityScale() : transform.localScale.x;
        _cachedThreat = FindEntityByScaleComparison(myScale, biggerThanMe: true);
        return _cachedThreat;
    }

    public Transform FindPrey()
    {
        if (Time.time - _lastPreyScan < ScanCacheDuration && _cachedPrey != null)
            return _cachedPrey;
        _lastPreyScan = Time.time;
        float myScale = _owner != null ? _owner.GetMyAuthorityScale() : transform.localScale.x;
        _cachedPrey = FindEntityByScaleComparison(myScale, biggerThanMe: false);
        return _cachedPrey;
    }

    public Transform FindTargetToChase()
    {
        Transform prey = FindPrey();
        if (prey != null) return prey;
        return FindNearestJelly();
    }

    public Transform FindNearestJelly()
    {
        if (Time.time - _lastJellyScan < ScanCacheDuration && _cachedJelly != null)
            return _cachedJelly;
        _lastJellyScan = Time.time;

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
        _cachedJelly = nearest;
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

            // ★ 이미 판 밖인 상대는 쫓지도, 무서워하지도 않는다.
            //   흡수당한 사람은 오브젝트가 꺼져 목록에서 빠지지만,
            //   초콜릿·낙사로 탈락한 사람은 <b>오브젝트가 살아 있어 목록에 남는다.</b>
            //   그대로 두면 봇이 시체를 쫓아가거나 시체를 피해 도망친다.
            if (p.IsOutOfPlay) continue;

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
            if (b.IsOutOfPlay) continue;      // 탈락·흡수 중인 봇도 대상이 아니다

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
