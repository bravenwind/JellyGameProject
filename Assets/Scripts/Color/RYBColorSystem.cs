// ============================================================
// RYBColorSystem.cs
// ============================================================
// 역할: RYB 감산혼합 색상 엔진
//
// - JellyColorType: 기본색(R/Y/B) + 혼합색(주황/초록/보라) + 특수(흰/검/없음)
// - RYBColor: RYB 공간에서 색상 누산, RGB 변환, 순도 계산
//
// 물감 모델:
//   빨강 + 노랑 = 주황
//   노랑 + 파랑 = 초록
//   빨강 + 파랑 = 보라
//   전부 섞으면 = 검정
// ============================================================

using UnityEngine;
using System;

// JellyColorType enum은 DataManager.cs에 정의됨

[Serializable]
public struct RYBColor
{
    [Range(0f, 1f)] public float r;
    [Range(0f, 1f)] public float y;
    [Range(0f, 1f)] public float b;

    public RYBColor(float r, float y, float b)
    {
        this.r = Mathf.Clamp01(r);
        this.y = Mathf.Clamp01(y);
        this.b = Mathf.Clamp01(b);
    }

    // ── 프리셋 ──
    public static RYBColor white => new RYBColor(0f, 0f, 0f);
    public static RYBColor black => new RYBColor(1f, 1f, 1f);

    public float Total => r + y + b;

    // ================================================================
    // 누산
    // ================================================================

    public RYBColor Add(RYBColor other)
        => new RYBColor(r + other.r, y + other.y, b + other.b);

    public RYBColor Add(float dr, float dy, float db)
        => new RYBColor(r + dr, y + dy, b + db);

    // ================================================================
    // RYB → RGB 변환 (Gosset trilinear interpolation)
    // ================================================================
    //
    // RYB 큐브 꼭짓점 8개를 RGB로 매핑한 뒤 삼선형 보간.
    // 결과: 물감 혼합과 유사한 색상을 Unity Color로 반환.

    // 꼭짓점 RGB 값 (튜닝 가능)
    //                            R     G     B
    static readonly Vector3 C000 = new(1.00f, 1.00f, 1.00f); // White
    static readonly Vector3 C100 = new(1.00f, 0.00f, 0.00f); // Red
    static readonly Vector3 C010 = new(1.00f, 1.00f, 0.00f); // Yellow
    static readonly Vector3 C001 = new(0.16f, 0.36f, 0.60f); // Blue
    static readonly Vector3 C110 = new(1.00f, 0.50f, 0.00f); // Orange  (R+Y)
    static readonly Vector3 C101 = new(0.50f, 0.00f, 0.50f); // Purple  (R+B)
    static readonly Vector3 C011 = new(0.00f, 0.66f, 0.20f); // Green   (Y+B)
    static readonly Vector3 C111 = new(0.20f, 0.09f, 0.00f); // Black   (R+Y+B)

    public Color ToRGB()
    {
        float ir = 1f - r, iy = 1f - y, ib = 1f - b;

        Vector3 rgb =
            C000 * (ir * iy * ib) +
            C100 * (r  * iy * ib) +
            C010 * (ir * y  * ib) +
            C001 * (ir * iy * b ) +
            C110 * (r  * y  * ib) +
            C101 * (r  * iy * b ) +
            C011 * (ir * y  * b ) +
            C111 * (r  * y  * b );

        return new Color(
            Mathf.Clamp01(rgb.x),
            Mathf.Clamp01(rgb.y),
            Mathf.Clamp01(rgb.z),
            1f
        );
    }

    // ================================================================
    // 순도 (Purity) 계산
    // ================================================================
    //
    // 목표 색상에 대해 "불필요한 성분"이 얼마나 섞였는지 측정.
    //   단색 — 나머지 2개가 불필요
    //   혼합색 — 나머지 1개가 불필요 + 두 성분의 균형
    //   흰색 — 전체 색 농도가 낮을수록 순도 높음

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

    /// <summary>단색 순도: wanted가 충분하고 unwanted가 적을수록 높음</summary>
    private float PuritySingle(float wanted, float unwanted)
    {
        float total = wanted + unwanted;
        if (total < 0.05f || wanted < 0.05f)
            return 0f;
        return Mathf.Clamp01(1f - unwanted / total);
    }

    /// <summary>혼합색 순도: 불순물 페널티 + 두 성분의 균형 페널티</summary>
    private float PurityMixed(float w1, float w2, float unwanted)
    {
        float total = w1 + w2 + unwanted;
        if (total < 0.05f)
            return 0f;

        float wantedSum = w1 + w2;
        if (wantedSum < 0.05f)
            return 0f;

        // 불순물 페널티 (unwanted가 높을수록 순도 ↓)
        float cleanness = 1f - unwanted / total;

        // 균형 페널티 (w1과 w2의 비율이 1:1에서 벗어날수록 순도 ↓)
        float balance = 1f - Mathf.Abs(w1 - w2) / wantedSum;

        return Mathf.Clamp01(cleanness * balance);
    }

    // ================================================================
    // 지배 색상 판별
    // ================================================================

    /// <summary>
    /// 현재 RYB 상태에서 가장 지배적인 색상 타입을 반환.
    /// 모든 후보의 순도를 비교해 가장 높은 것을 선택.
    /// </summary>
    public JellyColorType GetDominantType()
    {
        float total = Total;
        if (total < 0.1f)
            return JellyColorType.White;

        JellyColorType best = JellyColorType.None;
        float bestPurity = 0f;

        // 6가지 색상 후보 순도 비교
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
