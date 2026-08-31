Shader "Unlit/NewUnlitShader"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)

        _LeftColor("Left Color", Color) = (0.4, 0.8, 1, 1)
        _RightColor("Right Color", Color) = (0.0, 0.4, 1, 1)

            // UI Mask/Stencil support
            _StencilComp("Stencil Comparison", Float) = 8
            _Stencil("Stencil ID", Float) = 0
            _StencilOp("Stencil Operation", Float) = 0
            _StencilWriteMask("Stencil Write Mask", Float) = 255
            _StencilReadMask("Stencil Read Mask", Float) = 255
            _ColorMask("Color Mask", Float) = 15

            [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }

        SubShader
        {
            Tags
            {
                "Queue" = "Transparent"
                "IgnoreProjector" = "True"
                "RenderType" = "Transparent"
                "PreviewType" = "Plane"
                "CanUseSpriteAtlas" = "True"
            }

            Stencil
            {
                Ref[_Stencil]
                Comp[_StencilComp]
                Pass[_StencilOp]
                ReadMask[_StencilReadMask]
                WriteMask[_StencilWriteMask]
            }

            Cull Off
            Lighting Off
            ZWrite Off
            ZTest[unity_GUIZTestMode]
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask[_ColorMask]

            Pass
            {
                Name "Default"
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma target 2.0

                #include "UnityCG.cginc"
                #include "UnityUI.cginc"

                #pragma multi_compile __ UNITY_UI_CLIP_RECT
                #pragma multi_compile __ UNITY_UI_ALPHACLIP

                struct appdata_t
                {
                    float4 vertex   : POSITION;
                    float4 color    : COLOR;
                    float2 texcoord : TEXCOORD0;
                };

                struct v2f
                {
                    float4 vertex   : SV_POSITION;
                    fixed4 color : COLOR;
                    float2 uv       : TEXCOORD0;
                    float4 worldPos : TEXCOORD1;
                };

                sampler2D _MainTex;
                fixed4 _Color;
                fixed4 _LeftColor;
                fixed4 _RightColor;

                float4 _ClipRect;

                v2f vert(appdata_t v)
                {
                    v2f o;
                    o.worldPos = v.vertex;
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.uv = v.texcoord;
                    o.color = v.color * _Color;
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    // Sample sprite alpha (for masks, sliced sprites, etc.)
                    fixed4 tex = tex2D(_MainTex, i.uv);

                // Left->Right gradient by UV.x (0..1)
                fixed t = saturate(i.uv.x);
                fixed4 grad = lerp(_LeftColor, _RightColor, t);

                // Combine: keep sprite alpha, keep UI tint
                fixed4 col = grad * i.color;
                col.a *= tex.a;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
        }
}
