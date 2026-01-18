float4 _LightColor0;

// Calculates lighting for diffuse and specular, where a certain one
// can be selected with "type" (0 - specular, 1 - diffuse)
float3 GetLighting(float type, float3 worldPos, float3 normal, float shininess) {
	float3 lightDirection;
	float attenuation;
	float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos.xyz);
	normal = normalize(normal);
	if (_WorldSpaceLightPos0.w == 0.0) {
		attenuation = 1.0;
		lightDirection = normalize(_WorldSpaceLightPos0.xyz);
	}
	else {
		float3 vertexToLightSource = _WorldSpaceLightPos0.xyz - worldPos.xyz;
		float distance = length(vertexToLightSource);
		attenuation = 1.0 / distance;
		lightDirection = normalize(vertexToLightSource);
	}

	float3 diffuseReflection = attenuation * _LightColor0.rgb * max(0.0, dot(normal, lightDirection));

	shininess = clamp(shininess, 1, 1000);
	float3 reflection = reflect(lightDirection, normal);
	float3 specularReflection = pow(saturate(dot(reflection, -viewDir)), shininess);
	specularReflection *= _LightColor0;

	if (type == 0)
		return specularReflection;
	else if (type == 1)
		return diffuseReflection;
}

float3 GetLighting(float type, float3 worldPos, float3 normal) {
	return GetLighting(type, worldPos, normal, 0);
}

float GetDither(float2 pos, float factor) {
	float DITHER_THRESHOLDS[16] =
	{
		1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
		13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
		4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
		16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
	};

	// Dynamic indexing isn't allowed in WebGL for some weird reason so here's this strange workaround
	#ifdef SHADER_API_GLES3
	int i = (int(pos.x) % 4) * 4 + int(pos.y) % 4;
	for (int x = 0; x < 16; x++)
		if (x == i)
			return factor - DITHER_THRESHOLDS[x];
	return 0;
	#else
	return factor - DITHER_THRESHOLDS[(uint(pos.x) % 4) * 4 + uint(pos.y) % 4];
	#endif
}