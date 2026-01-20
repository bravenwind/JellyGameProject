using System.Collections.Generic;
using UnityEngine;

public class JellyDataDAO : MonoBehaviour
{
    // [변경] 파일 경로 상수 분리
    private const string ENEMY_CSV_FILE_NAME = "Data/EnemyJellyData";   // 기존 에너미 젤리
    private const string PLAYER_CSV_FILE_NAME = "Data/PlayerJellyData"; // 새로 추가될 플레이어 젤리

    // 데이터 캐싱용 리스트
    private List<JellyDataDTO> enemyDataList = new List<JellyDataDTO>();
    private List<JellyDataDTO> playerDataList = new List<JellyDataDTO>();

    // 기존: 에너미 데이터 로드 (함수 이름 유지 또는 명확하게 변경 가능, 여기선 유지)
    public List<JellyDataDTO> LoadJellyData()
    {
        return LoadCsvData(ENEMY_CSV_FILE_NAME, ref enemyDataList);
    }

    // [추가] 플레이어 데이터 로드
    public List<JellyDataDTO> LoadPlayerData()
    {
        return LoadCsvData(PLAYER_CSV_FILE_NAME, ref playerDataList);
    }

    // [리팩토링] CSV 로드 로직을 공통 함수로 분리
    private List<JellyDataDTO> LoadCsvData(string path, ref List<JellyDataDTO> targetList)
    {
        if (targetList.Count > 0) return targetList;

        TextAsset csvData = Resources.Load<TextAsset>(path);
        if (csvData == null)
        {
            Debug.LogError($"'{path}' 파일을 찾을 수 없습니다. Resources/{path}.csv 파일이 있는지 확인하세요.");
            return null;
        }

        string[] lines = csvData.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            JellyDataDTO dto = new JellyDataDTO(line);
            targetList.Add(dto);
        }

        return targetList;
    }
}