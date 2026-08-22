using UnityEngine;

public static class JellyShaderProps
{
    public const string FRESNEL_COLOR = "_FresnelColor";

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
