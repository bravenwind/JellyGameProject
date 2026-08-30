using UnityEngine;

/// <summary>
/// 표준 셰이더의 색 프로퍼티. 타일·소품처럼 URP Lit/Unlit을 쓰는 것들이 공유한다.
///
/// 이 프로젝트는 URP 전용이므로 _BaseColor 하나만 본다.
/// 빌트인의 _Color로 떨어지는 폴백은 두지 않는다 — 쓸 일이 없는 갈래를 남겨두면
/// 읽는 사람이 "언제 빌트인을 쓰지?"를 계속 되묻게 된다.
///
/// 젤리 전용 프로퍼티는 JellyShaderProps에 따로 있다.
/// </summary>
public static class TileColorProps
{
    public const string BASE_COLOR = "_BaseColor";

    public static readonly int BaseColorId = Shader.PropertyToID(BASE_COLOR);

    /// <summary>
    /// 이 렌더러의 색을 읽고 쓸 수 있나. 셰이더에 _BaseColor가 없으면 false.
    ///
    /// 없는 프로퍼티에 SetColor를 하면 아무 일도 안 일어나고, GetColor는 검정을 돌려준다.
    /// 둘 다 에러가 안 나므로 칠하기 전에 물어봐야 한다.
    /// </summary>
    public static bool HasColor(Renderer renderer)
    {
        return renderer != null
            && renderer.sharedMaterial != null
            && renderer.sharedMaterial.HasProperty(BaseColorId);
    }
}
