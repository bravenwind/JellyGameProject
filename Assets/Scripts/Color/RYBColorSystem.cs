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

/// <summary>
/// 젤리 색의 종류. 기본 3색(R/Y/B) + 혼합 3색 + 특수 3종.
///
/// ★ 예전엔 DataManager.cs에 있었다
///   설정 통에 색 타입이 정의돼 있어서, 이 파일에 "enum은 DataManager.cs에 있음"이라는
///   안내 주석을 달아둬야 했다. 위치를 알려주는 주석이 필요하다는 것 자체가 신호다.
///
/// ※ 값이 씬·프리팹에 정수로 직렬화된다. <b>순서를 바꾸거나 가운데를 지우지 말 것.</b>
///   새 색은 뒤에 붙인다.
/// </summary>
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

    // 꼭짓점 RGB 값
    //
    // ★ 이름의 숫자가 곧 가중치 공식이다
    //   C101 = (r=1, y=0, b=1) → 가중치는 r × (1-y) × b.
    //   자리가 1이면 그 값을, 0이면 (1 - 그 값)을 곱한다. 여덟 개를 다 더하면
    //   (ir+r)(iy+y)(ib+b) = 1 이라 정확히 가중평균이 된다.
    //
    // ★ 파랑 계열을 밝혔다
    //   예전 파랑은 (0.16, 0.36, 0.60)으로 밝기가 노랑의 1/3도 안 됐다.
    //   같은 개수를 먹어도 파랑 쪽 젤리만 칙칙해 보이던 원인이다.
    //   보라도 파랑에서 파생돼 똑같이 어두웠기에 함께 올렸다.
    //                            R     G     B
    static readonly Vector3 C000 = new(1.00f, 1.00f, 1.00f); // White
    static readonly Vector3 C100 = new(1.00f, 0.13f, 0.15f); // Red
    static readonly Vector3 C010 = new(1.00f, 0.92f, 0.20f); // Yellow
    static readonly Vector3 C001 = new(0.20f, 0.50f, 0.88f); // Blue    ← 밝힘
    static readonly Vector3 C110 = new(1.00f, 0.55f, 0.10f); // Orange  (R+Y)
    static readonly Vector3 C101 = new(0.62f, 0.25f, 0.78f); // Purple  (R+B) ← 밝힘
    static readonly Vector3 C011 = new(0.15f, 0.72f, 0.35f); // Green   (Y+B)
    static readonly Vector3 C111 = new(0.22f, 0.11f, 0.05f); // Black   (R+Y+B) — 물감처럼 진한 갈색

    // ── 보정값 ──
    //
    // 검정 꼭짓점에 주는 지수. 1이면 원래 삼선형 그대로다.
    // 크게 줄수록 "세 색이 다 진할 때만" 검어진다.
    private const float BlackBias = 2.0f;

    // 보간이 끝난 색의 채도를 이만큼 끌어올린다. 1이면 보정 없음.
    // 세 보정값 중 <b>체감이 가장 큰 레버</b>다 — 꼭짓점 색이나 BlackBias를 만지는 것보다
    // 이 값 하나를 바꾸는 편이 훨씬 크게 바뀐다. 1.5를 넘기면 형광색처럼 뜬다.
    private const float SaturationBoost = 1.35f;

    // 명도의 바닥. 아무리 섞여도 이보다 어두워지지 않는다.
    private const float ValueFloor = 0.14f;

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

        // ★ 검정 가중치만 눌러준다
        //   원래 w111 = r·y·b 라, 세 색이 0.5씩만 있어도 가중치가 0.125다.
        //   검정 꼭짓점은 거의 무채색이라 그 12.5%가 채도를 통째로 갉아먹었다.
        //   젤리 몇 개만 섞으면 진흙색이 되던 원인이다.
        //   지수를 씌우면 0.125 → 0.044로 줄고, 세 색이 다 1일 때는 그대로 1이라
        //   '전부 섞으면 검정'이라는 규칙은 지켜진다.
        float w111 = Mathf.Pow(r * y * b, BlackBias);

        //검정 가중치를 줄인 만큼 합이 1보다 작아진다. 나눠서 되돌려야
        //나머지 일곱 색에 비례 배분되고, 전체가 어두워지지 않는다
        float sum = w000 + w100 + w010 + w001 + w110 + w101 + w011 + w111;
        sum = Mathf.Max(sum, 1e-4f);

        Vector3 rgb =
            (C000 * w000 + C100 * w100 + C010 * w010 + C001 * w001 +
             C110 * w110 + C101 * w101 + C011 * w011 + C111 * w111) / sum;

        return Vivify(new Color(
            Mathf.Clamp01(rgb.x), Mathf.Clamp01(rgb.y), Mathf.Clamp01(rgb.z), 1f));
    }

    /// <summary>
    /// 보간 결과를 화면에서 보기 좋게 다듬는다.
    ///
    /// ★ 왜 필요한가
    ///   RGB 공간에서 직선으로 섞으면 두 색 사이가 <b>회색 쪽을 통과</b>한다.
    ///   물감은 실제로 그렇지만 게임에서는 그냥 탁해 보인다.
    ///   HSV로 바꿔 채도만 올리고 명도에 바닥을 깔아주면 색이 살아난다.
    /// </summary>
    private static Color Vivify(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);

        s = Mathf.Clamp01(s * SaturationBoost);

        //v를 [0,1] → [ValueFloor,1] 로 옮긴다. 새까맣게 죽는 색이 없어진다
        v = Mathf.Clamp01(v * (1f - ValueFloor) + ValueFloor);

        Color outC = Color.HSVToRGB(h, s, v);
        outC.a = c.a;
        return outC;
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
