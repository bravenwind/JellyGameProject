Shader "Liquid/Liquid"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_NormalMap("Normal Map", 2D) = "bump" {}
		_WavesTex("Waves", 2D) = "black" {}
		_PerlinNoise("Perlin Noise", 2D) = "black" {}
		_BubbleTex("Bubble", 2D) = "bump" {}
		_LiquidColor("Liquid Color", Color) = (1,1,1,1)
		_TopColor("Top Color", Color) = (1,1,1,1)
		_FoamColor("Foam Color", Color) = (1,1,1,1)
		_Refraction("Refraction Index", Float) = 0.5
		_ProbeLod("Murkiness", Float) = 0.05
		_Syrup("Syrup", Float) = 0
		_EdgeThickness("Edge Thickness", Float) = 0.02
		_FresnelPower("Fresnel Power", Float) = 1.5
		_MeniscusHeight("Meniscus Height", Float) = 0.04
		_MeniscusCurve("Meniscus Curve", Float) = 0.75
		_FoamAmount("Foam Amount", Float) = 1.0
		_BubbleScale("Bubble Scale", Float) = 1.0
		_BubbleCount("Maximum Bubbles", Float) = 30
		_UseGrabpass("Refraction Method", Float) = 0
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent-2" }
		LOD 100

		GrabPass {
			Tags { "LightMode"="Always" }
			"_BackgroundTexture"
		}

		// Back faces
		Pass
		{
			Tags { "LightMode"="ForwardBase" }
			Cull Front
			Blend SrcAlpha OneMinusSrcAlpha

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma shader_feature USE_GRABPASS
			
			#include "UnityCG.cginc"
			#include "UnityStandardConfig.cginc"
			#include "LiquidUtils.cginc"
			#include "Liquid.cginc"

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				o = vertex(v, -1);
				return o;
			}

			fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
			{
				float4 col;
				col = fragment(i, facing);
				return col;
			}
			
			ENDCG
		}

		// Front faces
		Pass
		{
			Tags{ "LightMode" = "ForwardBase" }
			Cull Back
			Blend SrcAlpha OneMinusSrcAlpha

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma shader_feature USE_GRABPASS
			
			#include "UnityCG.cginc"
			#include "UnityStandardConfig.cginc"
			#include "LiquidUtils.cginc"
			#include "Liquid.cginc"

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				o = vertex(v, 1);
				return o;
			}

			fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
			{
				float4 col;
				col = fragment(i, facing);
				return col;
			}
			
			ENDCG
		}

		Pass
		{
			Tags{ "LightMode" = "ForwardAdd" }
			Blend SrcAlpha One
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"
			#include "UnityStandardConfig.cginc"
			#include "LiquidUtils.cginc"
			#include "Liquid.cginc"

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				o = vertex(v, 0);
				return o;
			}

			fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
			{
				float4 col;
				col = fragment(i, facing);
				float shininess = 30 * (1 - _ProbeLod);
				col.rgb = GetLighting(0, i.worldPos, surfNormal, shininess);
				return col;
			}
			
			ENDCG
		}
	}
	CustomEditor "LiquidEditor"
}
