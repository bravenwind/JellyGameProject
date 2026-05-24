using System.Collections.Generic;

public struct RankingData
{
    public string name;
    public float size;       // 젤리의 반지름 혹은 최종 스케일 값
    public bool isLocalPlayer;
    public int killCount;    // 필요시 활용
}

public static class ResultDataCarrier
{
    // 상위권 3명의 데이터만 담아서 이동
    public static List<RankingData> TopRankings = new List<RankingData>();
}