using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class JellyData
{
    public int ID;
    public string Name;
    public Color Color; // Unity Color 구조체 사용
    public int ScaleLevel;
    public string Base;
    public string Normal;
    public string Mask;

    public JellyData(int id, string name, Color color, int scaleLevel, string baseMap, string normalMap, string maskMap)
    {
        this.ID = id;
        this.Name = name;
        this.Color = color;
        this.ScaleLevel = scaleLevel;
        this.Base = baseMap;
        this.Normal = normalMap;
        this.Mask = maskMap;
    }
}

public class JellyDAO
{
    // 변경: ID(int) 대신 Name(string)을 Key로 사용하는 해시맵(Dictionary)
    private Dictionary<string, JellyData> jellyDataDict = new Dictionary<string, JellyData>();

    public void LoadData(TextAsset csvFile)
    {
        jellyDataDict.Clear();

        if (csvFile == null)
        {
            Debug.LogError("CSV File is null!");
            return;
        }

        string[] lines = csvFile.text.Split(new char[] { '\n' });

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] data = lines[i].Split(',');

            if (data.Length < 7) continue;

            try
            {
                int id = int.Parse(data[0]);
                string name = data[1].Trim(); // Key로 사용할 이름

                string colorStr = data[2].Trim();
                Color color = ParseColor(colorStr);

                int scaleLevel = int.Parse(data[3]);
                string baseMap = data[4].Trim();
                string normalMap = data[5].Trim();
                string maskMap = data[6].Trim();

                JellyData jelly = new JellyData(id, name, color, scaleLevel, baseMap, normalMap, maskMap);

                // 변경: 이름을 Key로 하여 Dictionary에 저장
                if (!jellyDataDict.ContainsKey(name))
                {
                    jellyDataDict.Add(name, jelly);
                }
                else
                {
                    Debug.LogWarning($"Duplicate jelly name found: {name}. Skipping entry with ID {id}.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error parsing line {i}: {e.Message}");
            }
        }

        Debug.Log($"JellyData Loaded: {jellyDataDict.Count} entries.");
    }

    private Color ParseColor(string colorStr)
    {
        // 1. RGB 숫자 파싱 ("100/150/150")
        if (colorStr.Contains("/"))
        {
            string[] rgb = colorStr.Split('/');
            if (rgb.Length >= 3)
            {
                try
                {
                    float r = float.Parse(rgb[0]) / 255f;
                    float g = float.Parse(rgb[1]) / 255f;
                    float b = float.Parse(rgb[2]) / 255f;
                    float a = (rgb.Length > 3) ? float.Parse(rgb[3]) / 255f : 1.0f;
                    return new Color(r, g, b, a);
                }
                catch
                {
                    return Color.white;
                }
            }
        }

        // 2. 색상 이름 파싱
        switch (colorStr.ToLower())
        {
            case "red": return Color.red;
            case "blue": return Color.blue;
            case "green": return Color.green;
            case "cyan": return Color.cyan;
            case "magenta": return Color.magenta;
            case "yellow": return Color.yellow;
            case "white": return Color.white;
            case "black": return Color.black;
            case "gray": return Color.gray;
            case "grey": return Color.grey;
            case "clear": return Color.clear;
            default:
                if (ColorUtility.TryParseHtmlString(colorStr, out Color hexColor))
                    return hexColor;
                return Color.white;
        }
    }

    // 변경: 이름을 통해 데이터 가져오기
    public JellyData GetJellyData(string name)
    {
        if (jellyDataDict.ContainsKey(name))
        {
            return jellyDataDict[name];
        }
        else
        {
            Debug.LogWarning($"JellyData with name '{name}' not found.");
            return null;
        }
    }

    public List<JellyData> GetAllJellyData()
    {
        return new List<JellyData>(jellyDataDict.Values);
    }
}