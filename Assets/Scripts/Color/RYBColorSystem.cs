using UnityEngine;
using System;

public enum JellyColorType
{
    Red, Yellow, Blue,
    Orange, Green, Purple,
    White, Black, None
}

[Serializable]
public struct RYBColor
{
    [Range(0f, 1f)] public float r;
    [Range(0f, 1f)] public float y;
    [Range(0f, 1f)] public float b;

    // 이름의 숫자 = (r, y, b)
    //                            R     G     B
    static readonly Vector3 C000 = new(1.00f, 1.00f, 1.00f); // White
    static readonly Vector3 C100 = new(1.00f, 0.13f, 0.15f); // Red
    static readonly Vector3 C010 = new(1.00f, 0.92f, 0.20f); // Yellow
    static readonly Vector3 C001 = new(0.20f, 0.50f, 0.88f); // Blue
    static readonly Vector3 C110 = new(1.00f, 0.55f, 0.10f); // Orange  (R+Y)
    static readonly Vector3 C101 = new(0.62f, 0.25f, 0.78f); // Purple  (R+B)
    static readonly Vector3 C011 = new(0.15f, 0.72f, 0.35f); // Green   (Y+B)
    static readonly Vector3 C111 = new(0.22f, 0.11f, 0.05f); // Black   (R+Y+B)

    private const float BlackBias = 2.0f;

    private const float SaturationBoost = 1.35f;

    private const float ValueFloor = 0.14f;

    public RYBColor(float r, float y, float b)
    {
        this.r = Mathf.Clamp01(r);
        this.y = Mathf.Clamp01(y);
        this.b = Mathf.Clamp01(b);
    }

    public static RYBColor white => new RYBColor(0f, 0f, 0f);

    public float Total => r + y + b;

    public RYBColor Add(RYBColor other)
        => new RYBColor(r + other.r, y + other.y, b + other.b);

    public Color ToRGB()
    {
        float ir = 1f - r, iy = 1f - y, ib = 1f - b;

        float w000 = ir * iy * ib;
        float w100 = r  * iy * ib;
        float w010 = ir * y  * ib;
        float w001 = ir * iy * b;
        float w110 = r  * y  * ib;
        float w101 = r  * iy * b;
        float w011 = ir * y  * b;

        float w111 = Mathf.Pow(r * y * b, BlackBias);

        float sum = w000 + w100 + w010 + w001 + w110 + w101 + w011 + w111;
        sum = Mathf.Max(sum, 1e-4f);

        Vector3 rgb =
            (C000 * w000 + C100 * w100 + C010 * w010 + C001 * w001 +
             C110 * w110 + C101 * w101 + C011 * w011 + C111 * w111) / sum;

        return Vivify(new Color(
            Mathf.Clamp01(rgb.x), Mathf.Clamp01(rgb.y), Mathf.Clamp01(rgb.z), 1f));
    }

    private static Color Vivify(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);

        s = Mathf.Clamp01(s * SaturationBoost);

        v = Mathf.Clamp01(v * (1f - ValueFloor) + ValueFloor);

        Color outC = Color.HSVToRGB(h, s, v);
        outC.a = c.a;
        return outC;
    }

    public float GetPurity(JellyColorType targetType)
    {
        switch (targetType)
        {
            case JellyColorType.White:
                return Mathf.Clamp01(1f - Total);

            case JellyColorType.Red:    return PuritySingle(r, y + b);
            case JellyColorType.Yellow: return PuritySingle(y, r + b);
            case JellyColorType.Blue:   return PuritySingle(b, r + y);

            case JellyColorType.Orange: return PurityMixed(r, y, b);
            case JellyColorType.Green:  return PurityMixed(y, b, r);
            case JellyColorType.Purple: return PurityMixed(r, b, y);

            default: return 0f;
        }
    }

    private float PuritySingle(float wanted, float unwanted)
    {
        float total = wanted + unwanted;
        if (total < 0.05f || wanted < 0.05f)
            return 0f;
        return Mathf.Clamp01(1f - unwanted / total);
    }

    private float PurityMixed(float w1, float w2, float unwanted)
    {
        float total = w1 + w2 + unwanted;
        if (total < 0.05f)
            return 0f;

        float wantedSum = w1 + w2;
        if (wantedSum < 0.05f)
            return 0f;

        float cleanness = 1f - unwanted / total;

        float balance = 1f - Mathf.Abs(w1 - w2) / wantedSum;

        return Mathf.Clamp01(cleanness * balance);
    }

    public JellyColorType GetDominantType()
    {
        float total = Total;
        if (total < 0.1f)
            return JellyColorType.White;

        JellyColorType best = JellyColorType.None;
        float bestPurity = 0f;

        CheckCandidate(JellyColorType.Red,    ref best, ref bestPurity);
        CheckCandidate(JellyColorType.Yellow, ref best, ref bestPurity);
        CheckCandidate(JellyColorType.Blue,   ref best, ref bestPurity);
        CheckCandidate(JellyColorType.Orange, ref best, ref bestPurity);
        CheckCandidate(JellyColorType.Green,  ref best, ref bestPurity);
        CheckCandidate(JellyColorType.Purple, ref best, ref bestPurity);

        if (bestPurity < 0.35f)
            return total > 1.5f ? JellyColorType.Black : JellyColorType.None;

        return best;
    }

    private void CheckCandidate(JellyColorType type, ref JellyColorType best, ref float bestPurity)
    {
        float p = GetPurity(type);
        if (p > bestPurity)
        {
            bestPurity = p;
            best = type;
        }
    }
}
