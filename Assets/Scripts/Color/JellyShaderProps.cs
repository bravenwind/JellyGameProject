using UnityEngine;

public static class JellyShaderProps
{
    public const string FRESNEL_COLOR = "_FresnelColor";
    public const string BASE_COLOR_01 = "_BaseColor_01";
    public const string BASE_COLOR_02 = "_BaseColor_02";

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
