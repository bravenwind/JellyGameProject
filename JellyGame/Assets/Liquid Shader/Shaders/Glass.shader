Shader "Liquid/Glass"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "red" {}
		_NormalMap("Normal Map", 2D) = "bump" {}
		_RoughnessTex("Roughness", 2D) = "white" {}
		_GlassColor("Glass Tint", Color) = (1,1,1,1)
		_Blur("Blur", Float) = 1
		_FresnelPower("Fresnel Power", Float) = 3
		_NormalMult("Normal Map Multiplier", Float) = 1
		_Thickness("Thickness", Float) = 0.05
		_DrawBackface("Draw Backface", Float) = 1
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent-1" }
		LOD 100

		// Pass for backface rendering
		Pass
		{
			Tags { "LightMode"="ForwardBase" }

			Cull Front
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"
			#include "LiquidUtils.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
				float3 normal : TEXCOORD2;
				float3 worldPos : TEXCOORD3;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _Blur;
			float _Thickness;
			
			v2f vert (appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				o.vertex = UnityObjectToClipPos(v.vertex / (_Thickness + 1));
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.normal = normalize(mul(float4(v.normal, 0.0), unity_WorldToObject).xyz);
				o.worldPos = mul(unity_ObjectToWorld, v.vertex);
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}
			
			fixed4 frag (v2f i) : COLOR
			{
				float4 col = float4(0, 0, 0, 0);
				float3 diffuse = GetLighting(1, i.worldPos, i.normal);
				float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;

				col += tex2D(_MainTex, i.uv) * float4(diffuse + ambient, 1);

				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}

		// Horizontal grabpass blur
		GrabPass
		{
			Tags { "LightMode" = "Always" }
		}
		Pass
		{
			Tags { "LightMode" = "Always" }

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uvgp : TEXCOORD0;
				float2 uv : TEXCOORD1;
			};

			struct v2f
			{
				float4 uvgp : TEXCOORD0;
				float2 uv : TEXCOORD2;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			sampler2D _GrabTexture;
			float4 _GrabTexture_TexelSize;
			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;
			float _Blur;
			
			v2f vert (appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				o.vertex = UnityObjectToClipPos(v.vertex);
				#if UNITY_UV_STARTS_AT_TOP
                float scale = -1.0;
                #else
                float scale = 1.0;
                #endif
				o.uvgp.xy = (float2(o.vertex.x, o.vertex.y * scale) + o.vertex.w) * 0.5;
				o.uvgp.zw = o.vertex.zw;
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}
			
			fixed4 frag (v2f i) : COLOR
			{
				float4 col = float4(0, 0, 0, 0);
				#define GRABPIXEL(weight, kernelx) tex2Dproj(_GrabTexture, UNITY_PROJ_COORD(float4(i.uvgp.x + _GrabTexture_TexelSize.x * kernelx * _Blur, i.uvgp.y, i.uvgp.z, i.uvgp.w))) * weight

				col += GRABPIXEL(0.05, -4.0);
				col += GRABPIXEL(0.09, -3.0);
				col += GRABPIXEL(0.12, -2.0);
				col += GRABPIXEL(0.15, -1.0);
				col += GRABPIXEL(0.18, 0.0);
				col += GRABPIXEL(0.15, +1.0);
				col += GRABPIXEL(0.12, +2.0);
				col += GRABPIXEL(0.09, +3.0);
				col += GRABPIXEL(0.05, +4.0);

				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}

		// Vertical grabpass blur
		GrabPass
		{
			Tags { "LightMode" = "Always" }
		}
		Pass
		{
			Tags { "LightMode" = "Always" }

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uvgp : TEXCOORD0;
			};

			struct v2f
			{
				float4 uvgp : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			sampler2D _GrabTexture;
			float4 _GrabTexture_TexelSize;
			float4 _MainTex_ST;
			float _Blur;
			
			v2f vert (appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				o.vertex = UnityObjectToClipPos(v.vertex);
				#if UNITY_UV_STARTS_AT_TOP
                float scale = -1.0;
                #else
                float scale = 1.0;
                #endif
				o.uvgp.xy = (float2(o.vertex.x, o.vertex.y * scale) + o.vertex.w) * 0.5;
				o.uvgp.zw = o.vertex.zw;
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}
			
			fixed4 frag (v2f i) : COLOR
			{
				float4 col = float4(0, 0, 0, 0);
				#define GRABPIXEL(weight, kernely) tex2Dproj(_GrabTexture, UNITY_PROJ_COORD(float4(i.uvgp.x, i.uvgp.y + _GrabTexture_TexelSize.y * kernely * _Blur, i.uvgp.z, i.uvgp.w))) * weight

				col += GRABPIXEL(0.05, -4.0);
				col += GRABPIXEL(0.09, -3.0);
				col += GRABPIXEL(0.12, -2.0);
				col += GRABPIXEL(0.15, -1.0);
				col += GRABPIXEL(0.18, 0.0);
				col += GRABPIXEL(0.15, +1.0);
				col += GRABPIXEL(0.12, +2.0);
				col += GRABPIXEL(0.09, +3.0);
				col += GRABPIXEL(0.05, +4.0);

				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}
		
		// Main glass material stuff
		GrabPass
		{
			Tags { "LightMode" = "Always" }
		}
		Pass
		{
			Tags { "LightMode" = "Always" }

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"
			#include "UnityStandardConfig.cginc"
			#include "LiquidUtils.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 uvgp : TEXCOORD1;
				float3 normal : TEXCOORD2;
				float3 viewDir : TEXCOORD3;
				float3 worldNormal : TEXCOORD4;
				float2 uvnm : TEXCOORD5;
				half3 tspace0 : TEXCOORD6;
				half3 tspace1 : TEXCOORD7;
				half3 tspace2 : TEXCOORD8;
				float4 worldPos : TEXCOORD9;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			sampler2D _GrabTexture;
			sampler2D _RoughnessTex;
			sampler2D _NormalMap;
			float4 _GrabTexture_TexelSize;
			float4 _MainTex_ST;
			float4 _NormalMap_ST;
			float4 _GlassColor;
			float _Blur;
			float _FresnelPower;
			float _NormalMult;
			
			v2f vert (appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				half3 worldNormal = UnityObjectToWorldNormal(v.normal);
				half3 worldTangent = UnityObjectToWorldDir(v.tangent);
				half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				half3 worldBitangent = cross(worldNormal, worldTangent) * tangentSign;
				o.tspace0 = half3(worldTangent.x, worldBitangent.x, worldNormal.x);
				o.tspace1 = half3(worldTangent.y, worldBitangent.y, worldNormal.y);
				o.tspace2 = half3(worldTangent.z, worldBitangent.z, worldNormal.z);

				o.worldPos = mul(unity_ObjectToWorld, v.vertex);
				o.vertex = UnityObjectToClipPos(v.vertex);

				#if UNITY_UV_STARTS_AT_TOP
                float scale = -1.0;
                #else
                float scale = 1.0;
                #endif
				o.uvgp.xy = (float2(o.vertex.x, o.vertex.y * scale) + o.vertex.w) * 0.5;
				o.uvgp.zw = o.vertex.zw;

				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.uvnm = TRANSFORM_TEX(v.uv, _NormalMap);
				o.normal = normalize(mul(float4(v.normal, 0.0), unity_WorldToObject).xyz);
				o.worldNormal = UnityObjectToWorldNormal(v.normal);
				o.viewDir = mul(unity_ObjectToWorld, v.vertex).xyz - _WorldSpaceCameraPos;
				
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}
			
			fixed4 frag (v2f i) : COLOR
			{
				float4 normalMap = tex2D(_NormalMap, i.uvnm);
				half3 tangentNormal = UnpackNormal(normalMap);

				float2 distort = tangentNormal.rg;
				float2 offset = distort * _NormalMult * _GrabTexture_TexelSize.xy;
				i.uvgp.xy = offset * i.uvgp.z + i.uvgp.xy;

				
				half3 worldNormal;
				worldNormal.x = dot(i.tspace0, tangentNormal);
				worldNormal.y = dot(i.tspace1, tangentNormal);
				worldNormal.z = dot(i.tspace2, tangentNormal);
				worldNormal *= 0.5;

				float4 col = tex2Dproj(_GrabTexture, UNITY_PROJ_COORD(i.uvgp));
				float roughness = tex2D(_RoughnessTex, i.uv);

				float fresnel = pow(1.0 + dot(i.worldNormal, normalize(i.viewDir)), _FresnelPower);

				float3 reflectedDirection = reflect(i.viewDir, worldNormal);
				float4 envCubeReflect = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectedDirection, _Blur * 0.25 * UNITY_SPECCUBE_LOD_STEPS / roughness);

				float shininess = 10 / _Blur;
				float3 specular = GetLighting(0, i.worldPos, worldNormal, shininess);
				float3 diffuse = GetLighting(1, i.worldPos, worldNormal);
				float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;

				float4 main = tex2D(_MainTex, i.uv);

				col *= roughness;
				col *= envCubeReflect;
				col *= _GlassColor;
				col += fresnel * 0.5 * _GlassColor;

				col = lerp(col, main * float4(diffuse + ambient, 1), main.a);
				col.rgb += specular * roughness;;

				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}

		// Additional specular lighting
		Pass
		{
			Tags { "LightMode" = "ForwardAdd" }
			Blend SrcAlpha One

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"
			#include "UnityStandardConfig.cginc"
			#include "LiquidUtils.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 uvgp : TEXCOORD1;
				float3 normal : TEXCOORD2;
				float3 viewDir : TEXCOORD3;
				float3 worldNormal : TEXCOORD4;
				float2 uvnm : TEXCOORD5;
				half3 tspace0 : TEXCOORD6;
				half3 tspace1 : TEXCOORD7;
				half3 tspace2 : TEXCOORD8;
				float4 worldPos : TEXCOORD9;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			sampler2D _NormalMap;
			sampler2D _RoughnessTex;
			float4 _MainTex_ST;
			float4 _NormalMap_ST;
			float _Blur;
			
			v2f vert (appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				half3 worldNormal = UnityObjectToWorldNormal(v.normal);
				half3 worldTangent = UnityObjectToWorldDir(v.tangent);
				half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				half3 worldBitangent = cross(worldNormal, worldTangent) * tangentSign;
				o.tspace0 = half3(worldTangent.x, worldBitangent.x, worldNormal.x);
				o.tspace1 = half3(worldTangent.y, worldBitangent.y, worldNormal.y);
				o.tspace2 = half3(worldTangent.z, worldBitangent.z, worldNormal.z);

				o.worldPos = mul(unity_ObjectToWorld, v.vertex);
				o.vertex = UnityObjectToClipPos(v.vertex);

				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.uvnm = TRANSFORM_TEX(v.uv, _NormalMap);
				o.normal = normalize(mul(float4(v.normal, 0.0), unity_WorldToObject).xyz);
				
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}
			
			fixed4 frag (v2f i) : COLOR
			{
				float4 normalMap = tex2D(_NormalMap, i.uvnm);
				half3 tangentNormal = UnpackNormal(normalMap) / 2;

				half3 worldNormal;
				worldNormal.x = dot(i.tspace0, tangentNormal);
				worldNormal.y = dot(i.tspace1, tangentNormal);
				worldNormal.z = dot(i.tspace2, tangentNormal);

				float roughness = tex2D(_RoughnessTex, i.uv);

				float4 col = float4(0, 0, 0, 1);

				float shininess = 10 / _Blur;
				float3 specular = GetLighting(0, i.worldPos, worldNormal, shininess);
				float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;

				col.rgb = specular * roughness;

				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}

		Pass 
		{
			Tags { "LightMode" = "ShadowCaster" }

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_shadowcaster
			#include "UnityCG.cginc"
			#include "LiquidUtils.cginc"

			struct appdata {
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct v2f {
				V2F_SHADOW_CASTER;
				float2 uv : TEXCOORD1;
			};
			struct v2f_fragment {
				UNITY_VPOS_TYPE vpos : VPOS;
				float2 uv : TEXCOORD1;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _GlassColor;

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);

				o.pos = UnityObjectToClipPos(v.vertex);

				TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
					
				return o;
			}

			float4 frag(v2f_fragment i) : SV_Target
			{
				fixed4 col = 1-_GlassColor/2;
				float luminance = col.r * 0.3 + col.g * 0.59 + col.b * 0.11;
				clip(GetDither(i.vpos, luminance) * luminance);

				SHADOW_CASTER_FRAGMENT(i);
			}
			ENDCG
		}
	}
	CustomEditor "GlassEditor"
}
