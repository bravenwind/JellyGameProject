using UnityEngine;

/// <summary>
/// 젤리 셰이더의 프로퍼티 이름·ID를 모아두는 단일 출처.
///
/// ★ 왜 한 곳에 모으는가
///   셰이더 프로퍼티는 문자열로 찾으면 오타가 나도 <b>컴파일이 통과한다.</b>
///   런타임에 색만 안 바뀌고 끝나서, 화면을 보고 역추적해야 원인을 찾는다.
///   여기 모아두면 오타는 컴파일 오류가 되고, 셰이더에서 이름이 바뀌어도 한 줄만 고치면 된다.
///
/// ★ Shader.PropertyToID는 정적 필드 초기화에서 불러도 된다
///   NavMesh.GetAreaFromName 같은 것과 달리 MonoBehaviour 생성자 제약을 받지 않는다.
///   Animator.StringToHash도 같은 부류다.
/// </summary>
public static class JellyShaderProps
{
    public const string BASE_COLOR_01 = "_BaseColor_01";
    public const string BASE_COLOR_02 = "_BaseColor_02";
    public const string FRESNEL_COLOR = "_FresnelColor";

    public static readonly int BaseColor01Id = Shader.PropertyToID(BASE_COLOR_01);
    public static readonly int BaseColor02Id = Shader.PropertyToID(BASE_COLOR_02);
    public static readonly int FresnelColorId = Shader.PropertyToID(FRESNEL_COLOR);

    public static Color ReadFresnel(Renderer r)
    {
        if (r == null)
            return Color.white;

        Material m = r.sharedMaterial;

        if (m == null || !m.HasProperty(FresnelColorId))
            return Color.white;

        return m.GetColor(FresnelColorId);
    }
}
