using System;
using UnityEngine;

[Serializable]
public class JellyDataDTO
{
    public int ID;
    public string Name;
    public JellyColorType ColorType;
    public int ColorIntensity;

    // [변경됨] RGB 값을 저장할 변수 (0~255)
    public int R;
    public int G;
    public int B;

    public string BaseMap;
    public string NormalMap;
    public string MaskMap;
    public string MaterialPath;

    public JellyDataDTO(string csvLine)
    {
        // 쉼표로 CSV 데이터 분리
        string[] values = csvLine.Split(',');

        try
        {
            // 컬럼 순서: ID, Name, ColorType, ColorIntensity, RGB, BaseMap, NormalMap, MaskMap, Material
            ID = int.Parse(values[0]);
            Name = values[1];
            ColorType = (JellyColorType)Enum.Parse(typeof(JellyColorType), values[2]);
            ColorIntensity = int.Parse(values[3]);

            // [핵심 변경] RGB 문자열 파싱 ("255/255/255" -> R, G, B)
            string rgbString = values[4];
            string[] rgbValues = rgbString.Split('/'); // 슬래시로 분리

            if (rgbValues.Length == 3)
            {
                R = int.Parse(rgbValues[0]);
                G = int.Parse(rgbValues[1]);
                B = int.Parse(rgbValues[2]);
            }
            else
            {
                Debug.LogWarning($"RGB 포맷 오류 (ID: {ID}): {rgbString}");
                // 오류 시 기본값 (흰색 등) 설정
                R = 255; G = 255; B = 255;
            }

            BaseMap = values[5];
            NormalMap = values[6];
            MaskMap = values[7];
            MaterialPath = values[8].Trim();
        }
        catch (Exception e)
        {
            Debug.LogError($"CSV 파싱 오류 (Line: {csvLine}) : {e.Message}");
        }
    }

    /// <summary>
    /// DTO의 RGB 값(0~255)을 Unity Color(0.0~1.0)로 변환하여 반환
    /// </summary>
    public Color GetColor()
    {
        return new Color(R / 255f, G / 255f, B / 255f, 1f);
    }
}