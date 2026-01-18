#define EPSILON 1.192092896e-07

struct appdata
{
	float3 normal : NORMAL;
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
	float4 tangent : TANGENT;
};

struct v2f
{
	float2 uv : TEXCOORD0;
	UNITY_FOG_COORDS(1)
	float4 pos : SV_POSITION;
	float3 worldPos : TEXCOORD1;
	#ifdef USE_GRABPASS
	float4 grabPos : TEXCOORD2;
	#endif
	float3 viewDir : TEXCOORD4;
	float3 normal : TEXCOORD5;
	half3 tspace0 : TEXCOORD6;
	half3 tspace1 : TEXCOORD7;
	half3 tspace2 : TEXCOORD8;
};

sampler2D _MainTex;
sampler2D _BackgroundTexture;
sampler2D _NormalMap;
sampler2D _WavesTex;
sampler2D _BubbleTex;
sampler2D _PerlinNoise;
float4 _MainTex_ST;
float _Refraction;
float4 _LiquidColor;
float4 _TopColor;
float4 _FoamColor;
float _BoundsL;
float _BoundsH;
float _BoundsX;
float _BoundsZ;
float _ProbeLod;
float _EdgeThickness;
float _WavesMult;
float _FresnelIntensity;
float _FresnelPower;
float _MeshScale;
float _MeniscusHeight;
float _MeniscusCurve;
float _Syrup;
float _Foam;
float _FoamAmount;
float _BubbleScale;
float _BubbleCount;
float _UseGrabpass;
float4 _Plane;
float3 _PlanePos;

half3 surfNormal;
	
struct Triplanar {
	float2 x, y, z;
};

Triplanar GetTriplanar(float3 worldPos) {
	Triplanar tri;
	tri.x = worldPos.zy;
	tri.y = worldPos.xz;
	tri.z = worldPos.xy;
	return tri;
}

// XZ representation of a texture
float4 BiplanarTex(sampler2D tex, float3 worldPos, float2 scale, float3 offset) {
	float4 x = tex2D(tex, (worldPos.yz + offset.yz) * scale);
	float4 z = tex2D(tex, (worldPos.xy + offset.xy) * scale);
	return x + z;
}

float4 TriplanarTex(sampler2D tex, float3 worldPos, float3 normal, float2 scale, float3 offset) {
	normal = abs(normal);
	float3 weights = normal / (normal.x + normal.y + normal.z);
	float4 x = tex2D(tex, (worldPos.yz + offset.yz) * scale);
	float4 y = tex2D(tex, (worldPos.xz + offset.xz) * scale);
	float4 z = tex2D(tex, (worldPos.xy + offset.xy) * scale);
	return weights.x * x + weights.y * y + weights.z + z;
}

float GetFresnel(float3 normal, float3 viewDir, float facing, float power, float intensity) {
	float dotProduct = 1 - pow(dot(normal, normalize(facing*viewDir)), power) * intensity;
	float4 fresnelCol = smoothstep(0.5, 1.0, dotProduct);
	float fresnel = saturate(fresnelCol);
	return fresnel;
}

float Random(float value) {
	return tex2D(_PerlinNoise, float2(value/256, (value+1)/256)).rgb;
}

float CalculateWaves(v2f i, fixed facing) {
	float fresnel = GetFresnel(i.normal, i.viewDir, facing, _MeniscusCurve, 0.5);
	float4 wavesTex = BiplanarTex(_WavesTex, i.worldPos, 0.25 / _MeshScale, -_Time.x * 10 - mul(unity_ObjectToWorld, float4(0, 0, 0, 1)));
	float waves = saturate(wavesTex.rgb) - 0.5;
	waves = waves * 0.005 * pow(_WavesMult, 5) * (1 + fresnel) * _MeshScale - (_WavesMult - 1)*0.1;
	return waves;
}

v2f vertex (appdata v, fixed facing)
{
	v2f o;

	half3 worldNormal = UnityObjectToWorldNormal(v.normal);
	half3 worldTangent = UnityObjectToWorldDir(v.tangent);
	half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
	half3 worldBitangent = cross(worldNormal, worldTangent) * tangentSign;
	o.tspace0 = half3(worldTangent.x, worldBitangent.x, worldNormal.x);
	o.tspace1 = half3(worldTangent.y, worldBitangent.y, worldNormal.y);
	o.tspace2 = half3(worldTangent.z, worldBitangent.z, worldNormal.z);

	o.worldPos = mul(unity_ObjectToWorld, v.vertex);
	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = TRANSFORM_TEX(v.uv, _MainTex);
	o.normal = UnityObjectToWorldNormal(v.normal);
	o.viewDir = normalize(WorldSpaceViewDir(v.vertex));	

	#ifdef USE_GRABPASS
	float3 normal = v.normal;
	float viewDirMult = 1;
	if (facing < 0) {
		normal = float3(0, 1, 0);
		viewDirMult = -1;
	}
	float3 refraction = normalize(refract(-WorldSpaceViewDir(v.vertex) * viewDirMult, UnityObjectToWorldNormal(normal), 1.0 / _Refraction));
	float3 objRefraction = mul(unity_WorldToObject, refraction) * 8;
	float4 vertexPos = UnityObjectToClipPos(float4(objRefraction, v.vertex.w));
	o.grabPos = ComputeGrabScreenPos(vertexPos);
	COMPUTE_EYEDEPTH(o.grabPos.z);
	#endif

	UNITY_TRANSFER_FOG(o,o.vertex);
	return o;
}
			
fixed4 fragment (v2f i, fixed facing : VFACE) : SV_Target
{
	fixed4 col = tex2D(_MainTex, i.uv);
	UNITY_APPLY_FOG(i.fogCoord, col);
	float4 colorAdd = float4(0, 0, 0, 0);
			
	float fresnel = GetFresnel(i.normal, i.viewDir, facing, _MeniscusCurve, 0.5);
	float height = (_BoundsH - _BoundsL);

	float waves = CalculateWaves(i, facing);
	
	//Get cutoff plane
	float distance = dot(i.worldPos, _Plane.xyz);
	distance += _Plane.w + waves / (_WavesMult + 1) / (_WavesMult + 1);

	// Meniscus
	float increment = _EdgeThickness * 0.33;
	float edgeOffset = fresnel * _MeniscusHeight + _EdgeThickness;
	colorAdd = lerp(float4(0, 0, 0, 0), float4(0.35, 0.35, 0.35, 0), saturate((distance - edgeOffset + increment * 3) * 75));
	colorAdd = lerp(colorAdd, float4(-0.35, -0.35, -0.35, 0), saturate((distance - edgeOffset + increment * 2.5) * 75));
	colorAdd = lerp(colorAdd, float4(0, 0, 0, -0.5), saturate((distance - edgeOffset + increment * 1.6) * 75));
	colorAdd = lerp(colorAdd, float4(0, 0, 0, -0.5), saturate((distance - edgeOffset + increment * waves * 20) * 75));
	colorAdd = lerp(colorAdd, float4(0, 0, 0, -1), saturate((distance - edgeOffset + increment * waves * 25) * 75));

	// Calculate normals
	float4 normalMap = BiplanarTex(_NormalMap, i.worldPos, 1, -mul(unity_ObjectToWorld, float4(0, 0, 0, 1)));
	half3 tangentNormal;
	if (facing > 0)
		tangentNormal = UnpackNormal(tex2D(_NormalMap, i.uv));
	else
		tangentNormal = UnpackNormal(normalMap);
	half3 worldNormal;
	worldNormal.x = dot(i.tspace0, tangentNormal);
	worldNormal.y = dot(i.tspace1, tangentNormal);
	worldNormal.z = dot(i.tspace2, tangentNormal);

	// Bubbles
	float4 bubbles;
	float bubbleDistance = saturate(distance * 3 + 1);
	float numBubbles = _BubbleCount * (_WavesMult/2 - 1);
	numBubbles = clamp(numBubbles, 0, _BubbleCount);

	float perlin = BiplanarTex(_PerlinNoise, i.worldPos, 2, float3(_SinTime.x + 1, _CosTime.z + 2, _SinTime.y + 3)).rgb;
	for (int j = 1; j < numBubbles; j++) {
		float3 bubblePos = float3(sin(_Time.w + j) * _BoundsX / 3 + perlin / 40, height / 2 - ((_Time.y + j*(height*0.1)) % lerp(0, height, _PlanePos.y + 0.55)), cos(_Time.w - j) * _BoundsZ / 3 + perlin / 40) - mul(unity_ObjectToWorld, float4(0, 0, 0, 1));
		float2 bubbleScale = 25.0 / _BubbleScale + j;

		float4 bubble0 = BiplanarTex(_BubbleTex, i.worldPos, bubbleScale, bubblePos);
		float4 bubble1 = BiplanarTex(_BubbleTex, i.worldPos, bubbleScale * (1.0 / j + 3), bubblePos + 0.01);
		float4 bubble2 = BiplanarTex(_BubbleTex, i.worldPos, bubbleScale * (1.0 / j + 2), bubblePos - 0.02);

		bubbles.rgb += bubble0.rgb * bubble0.a + bubble1.rgb * bubble1.a + bubble2.rgb * bubble2.a;
		bubbles.a += (bubble0.a + bubble1.a + bubble2.a);
	}

	// Differentiate back face and front face normals
	surfNormal = worldNormal;
	half3 topNormal = normalize(half3(_Plane.x + waves * 10, _Plane.y, _Plane.z + waves * 10));
	if (facing < 0) {
		// If backface, make its normal face up
		surfNormal = topNormal;
		surfNormal = lerp(surfNormal, -worldNormal, saturate((distance - edgeOffset + increment * 3) * 25));
	} else {
		// If front face, make the surface edge's normal lerp to up
		surfNormal = lerp(surfNormal, topNormal, saturate((distance - edgeOffset + increment * 3) * 25));
		surfNormal = lerp(surfNormal, worldNormal, saturate((distance - edgeOffset + _EdgeThickness / 3) * 100));
		surfNormal.x *= (bubbles.rgb * bubbleDistance * 4 + 1);
		surfNormal.y *= (bubbles.rgb * bubbleDistance * 4 + 1);
		surfNormal.z *= (bubbles.rgb * bubbleDistance * 4 + 1);
	}


	// Meniscus gets some extra refraction
	_Refraction = lerp(_Refraction, _Refraction + 0.5, saturate((distance - edgeOffset + increment * 3) * 25));

	// Calculate refraction and reflection from a reflection probe's cubemaps
	#ifdef USE_GRABPASS
	float3 refractedDirection = refract(-normalize(i.viewDir), surfNormal, 1.0 / _Refraction);
	half3 refraction = tex2Dproj(_BackgroundTexture, UNITY_PROJ_COORD(i.grabPos)).rgb;
	half3 reflection = tex2Dproj(_BackgroundTexture, 1-UNITY_PROJ_COORD(i.grabPos)).rgb;
	#else
	float3 refractedDirection = refract(-normalize(i.viewDir), surfNormal, 1.0 / _Refraction);
	float3 reflectedDirection = reflect(i.viewDir, normalize(surfNormal));
	float4 envCubeRefract = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, refractedDirection, _ProbeLod * UNITY_SPECCUBE_LOD_STEPS);
	float4 envCubeReflect = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, -reflectedDirection, _ProbeLod * UNITY_SPECCUBE_LOD_STEPS);
	half3 refraction = DecodeHDR(envCubeRefract, unity_SpecCube0_HDR);
	half3 reflection = DecodeHDR(envCubeReflect, unity_SpecCube0_HDR);
	#endif

	col.rgb *= refraction * (1 - _Syrup);
	col.rgb += _Syrup;

	// Calculate lighting stuff
	float shininess = 30 * (1 - _ProbeLod);
	float3 specularReflection = GetLighting(0, i.worldPos, surfNormal, shininess);
	float3 diffuseReflection = GetLighting(1, i.worldPos, surfNormal);
	float3 ambientLighting = UNITY_LIGHTMODEL_AMBIENT.rgb;

	_Foam = clamp(_Foam, 0, _FoamAmount * 0.03);
	float4 noise = TriplanarTex(_WavesTex, i.worldPos, surfNormal, float2(1, 1), float3(0, waves, 0));
	if (facing > 0) {
		// If front face
		float refresnel = GetFresnel(i.normal, i.viewDir, facing, _FresnelPower, 1);
		col.rgb *= (1 - refresnel);
		col.rgb += reflection * refresnel;
		col.rgb *= _LiquidColor;
		col += colorAdd;
		col.rgb += specularReflection;
		col.rgb -= bubbles.a / 4 * bubbleDistance;
		col.rgb += saturate(bubbles.rgb) / 4 * bubbleDistance;
		col = lerp(col, _FoamColor * float4((diffuseReflection + ambientLighting) *noise*0.25+0.5, 1), saturate((distance - edgeOffset + _EdgeThickness / 3) * 100) * saturate(_Foam * 100));
		col = lerp(col, float4(col.r, col.g, col.b, 0), saturate((distance/6 - _Foam - edgeOffset/6 + _EdgeThickness / 18) * 600));
	} else {
		// If back face, act as liquid's surface
		float bfFresnel = pow(1 + dot(-normalize(i.viewDir), _Plane), _FresnelPower * 0.35);
		col.rgb *= (1 - bfFresnel);
		col.rgb += reflection * bfFresnel;
		col.rgb *= _TopColor;
		col = lerp(col, float4(col.r, col.g, col.b, 0), saturate((distance - edgeOffset + increment * 1.6) * 100));
		col.rgb += specularReflection * bfFresnel;
		col.a = lerp(col.a, 1, saturate((distance - edgeOffset + _EdgeThickness) * 100));
		col.a = lerp(col.a, 0, saturate((distance/6 - _Foam - edgeOffset/6 + _EdgeThickness / 18) * 600));
		col.rgb = lerp(col, _FoamColor * (bfFresnel*0.5+0.5), saturate(_Foam*100));
	}

	if (col.a <= 0) discard;


	return col;
}