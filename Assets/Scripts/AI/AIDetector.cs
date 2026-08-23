using UnityEngine;

public class AIDetector : MonoBehaviour
{
    [Header("탐지")]
    public float detectRadius = 15f;
    public float baseAgentRadius = 0.5f;

    private AIPlayerMovement _owner;

    // ★ 캐시는 "언제 스캔했나"만 보고 걸린다
    //   예전엔 조건이 `경과 < 0.1f && _cached != null` 이었다. 결과가 null이면
    //   캐시가 절대 안 걸리는데, "주변에 아무도 없음"이야말로 가장 흔한 상태다.
    //   즉 캐시가 가장 필요한 순간에 정확히 빗나가서, 봇 하나가 초당 17번
    //   (긴급 위협 0.1초 + 상태평가 0.15초 + 추격 재평가 0.5초) 전부 전체 순회를 했다.
    //   "없다"도 엄연한 답이므로 그대로 캐시한다.
    private const float ScanCacheDuration = 0.1f;
    private Transform _cachedThreat;
    private Transform _cachedPrey;
    private Transform _cachedJelly;

    //-1이 아니라 float.NegativeInfinity로 둬야 Time.time이 0.05인 첫 프레임에도
    //"오래전에 쟀다"로 읽혀 첫 스캔이 반드시 돈다
    private float _lastThreatScan = float.NegativeInfinity;
    private float _lastPreyScan = float.NegativeInfinity;
    private float _lastJellyScan = float.NegativeInfinity;

    private void Awake()
    {
        _owner = GetComponent<AIPlayerMovement>();
    }

    public Transform FindThreat()
    {
        if (Time.time - _lastThreatScan < ScanCacheDuration)
            return _cachedThreat;
        _lastThreatScan = Time.time;
        _cachedThreat = FindEntityByScaleComparison(MyScale, biggerThanMe: true);
        return _cachedThreat;
    }

    public Transform FindPrey()
    {
        if (Time.time - _lastPreyScan < ScanCacheDuration)
            return _cachedPrey;
        _lastPreyScan = Time.time;
        _cachedPrey = FindEntityByScaleComparison(MyScale, biggerThanMe: false);
        return _cachedPrey;
    }

    private float MyScale
    {
        get { return _owner != null ? _owner.GetMyAuthorityScale() : transform.localScale.x; }
    }

    public Transform FindTargetToChase()
    {
        Transform prey = FindPrey();
        if (prey != null) return prey;
        return FindNearestJelly();
    }

    public Transform FindNearestJelly()
    {
        if (Time.time - _lastJellyScan < ScanCacheDuration)
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

        //EntityRegistry.Players에는 LanPlayerState(사람 프리팹 전용)만 들어온다.
        //봇은 Bots에 따로 등록되므로 여기서 '나 자신'이 나올 수 없다 —
        //예전엔 그걸 GetComponentInParent<AIPlayerMovement>()로 매번 확인했는데,
        //항상 null인 계층 탐색을 사람 수 × 스캔 횟수만큼 돌리는 순수 낭비였다
        foreach (var p in EntityRegistry.Players)
        {
            if (p == null) continue;

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
