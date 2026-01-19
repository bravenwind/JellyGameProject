using System.Collections.Generic;
using UnityEngine;

public class JellyDataDAO : MonoBehaviour
{
    private const string CSV_FILE_NAME = "Data/JellyData"; // Resources 폴더 파일명

    private List<JellyDataDTO> jellyDataList = new List<JellyDataDTO>();

    public List<JellyDataDTO> LoadJellyData()
    {
        if (jellyDataList.Count > 0) return jellyDataList;

        TextAsset csvData = Resources.Load<TextAsset>(CSV_FILE_NAME);
        if (csvData == null)
        {
            Debug.LogError($"'{CSV_FILE_NAME}' 파일을 찾을 수 없습니다.");
            return null;
        }

        string[] lines = csvData.text.Split('\n');

        // 헤더 건너뛰고 1부터 시작
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            JellyDataDTO dto = new JellyDataDTO(line);
            jellyDataList.Add(dto);
        }

        return jellyDataList;
    }
}