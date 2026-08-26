using UnityEngine;
using JellyNet;

public class AIDetector : MonoBehaviour
{
    // ★ 두 값 다 AIPlayerMovement가 넣어준다
    //   인스펙터에 따로 적어두면 봇 본체와 탐지기의 숫자가 조용히 벌어진다.
    //   detectRadius는 봇의 설정값, baseAgentRadius는 NavMeshAgent가 원본이다.
    private float detectRadius = 15f;
    private float baseAgentRadius = 0.5f;

    /// <summary>봇 본체가 Awake에서 한 번 넣어준다.</summary>
    public void Configure(float detectRadius, float baseAgentRadius)
    {
        this.detectRadius = detectRadius;
        this.baseAgentRadius = baseAgentRadius;
    }

    private AIPlayerMovement owner;

    // ★ 캐시는 "언제 스캔했나"만 보고 걸린다
    //   예전엔 조건이 `경과 < 0.1f && cached != null` 이었다. 결과가 null이면
    //   캐시가 절대 안 걸리는데, "주변에 아무도 없음"이야말로 가장 흔한 상태다.
    //   즉 캐시가 가장 필요한 순간에 정확히 빗나가서, 봇 하나가 초당 17번
    //   (긴급 위협 0.1초 + 상태평가 0.15초 + 추격 재평가 0.5초) 전부 전체 순회를 했다.
    //   "없다"도 엄연한 답이므로 그대로 캐시한다.
    private const float ScanCacheDuration = 0.1f;
    private Transform cachedThreat;
    private Transform cachedPrey;
    private Transform cachedJelly;

    //-1이 아니라 float.NegativeInfinity로 둬야 Time.time이 0.05인 첫 프레임에도
    //"오래전에 쟀다"로 읽혀 첫 스캔이 반드시 돈다
    private float lastThreatScan = float.NegativeInfinity;
    private float lastPreyScan = float.NegativeInfinity;
    private float lastJellyScan = float.NegativeInfinity;

    private void Awake()
    {
        owner = GetComponent<AIPlayerMovement>();
    }

    public Transform FindThreat()
    {
        if (Time.time - lastThreatScan < ScanCacheDuration)
            return cachedThreat;
        lastThreatScan = Time.time;
        cachedThreat = FindEntityByScaleComparison(MyScale, biggerThanMe: true);
        return cachedThreat;
    }

    public Transform FindPrey()
    {
        if (Time.time - lastPreyScan < ScanCacheDuration)
            return cachedPrey;
        lastPreyScan = Time.time;
        cachedPrey = FindEntityByScaleComparison(MyScale, biggerThanMe: false);
        return cachedPrey;
    }

    private float MyScale
    {
        get { return owner != null ? owner.GetMyAuthorityScale() : transform.localScale.x; }
    }

    public Transform FindTargetToChase()
    {
        Transform prey = FindPrey();
        if (prey != null)
            return prey;
        return FindNearestJelly();
    }

    public Transform FindNearestJelly()
    {
        if (Time.time - lastJellyScan < ScanCacheDuration)
            return cachedJelly;
        lastJellyScan = Time.time;

        Transform nearest = null;
        float minDist = detectRadius;

        foreach (var j in EntityRegistry.Jellies)
        {
            if (j == null)
                continue;
            float d = Vector3.Distance(transform.position, j.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = j.transform;
            }
        }
        cachedJelly = nearest;
        return nearest;
    }

    private Transform FindEntityByScaleComparison(float myScale, bool biggerThanMe)
    {
        Transform closest = null;
        float minEdgeDist = float.MaxValue;

        //★ 사람과 봇을 한 벌로 돈다
        //  예전엔 Players 한 번, Bots 한 번이었다. 그런데 두 루프의 본문이
        //  '크기를 비교해 가장 가까운 상대를 고른다'로 완전히 같으면서
        //  크기를 읽는 줄만 p.ScaleValue / b.GetMyAuthorityScale()로 갈려 있었다.
        //  INetEntity.ScaleValue가 그 갈림을 안으로 삼켰다.
        foreach (INetEntity e in EntityRegistry.Entities)
        {
            if (e == null || e.Transform == null || e.Transform == transform)
                continue;

            // ★ 이미 판 밖인 상대는 쫓지도, 무서워하지도 않는다.
            //   흡수당한 개체는 오브젝트가 꺼져 목록에서 빠지지만,
            //   초콜릿·낙사로 탈락한 쪽은 <b>오브젝트가 살아 있어 목록에 남는다.</b>
            //   그대로 두면 봇이 시체를 쫓아가거나 시체를 피해 도망친다.
            if (e.IsOutOfPlay)
                continue;

            float otherScale = e.ScaleValue;
            if (!IsScaleMatch(myScale, otherScale, biggerThanMe))
                continue;

            float edgeDist = CalcEdgeDistance(e.Transform.position, myScale, otherScale);
            if (edgeDist < detectRadius && edgeDist < minEdgeDist)
            {
                minEdgeDist = edgeDist;
                closest = e.Transform;
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
