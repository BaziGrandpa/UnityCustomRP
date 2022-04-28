#ifndef CUSTOM_BRDF_INCLUDED
#define CUSTOM_BRDF_INCLUDED

#define MIN_REFLECTIVITY 0.04

struct BRDF {
	float3 diffuse;
	float3 specular;
	float roughness;
};

float OneMinusReflectivity (float metallic) {
	float range = 1.0 - MIN_REFLECTIVITY;
	return range - metallic * range;//range*(1-metallic)
}

//计算CookTorrance BRDF 高光强度，注意这里只是获取到了强度，并非最终的高光项贡献
float SpecularStrength (Surface surface, BRDF brdf, Light light) {
	float3 h = SafeNormalize(light.direction + surface.viewDirection);//半程向量
	float nh2 = Square(saturate(dot(surface.normal, h)));//半程和法线夹角
	float lh2 = Square(saturate(dot(light.direction, h)));//半程和光源夹角
	float r2 = Square(brdf.roughness);
	float d2 = Square(nh2 * (r2 - 1.0) + 1.00001);
	float normalization = brdf.roughness * 4.0 + 2.0;
	return r2 / (d2 * max(0.1, lh2) * normalization);
}


BRDF GetBRDF (Surface surface, bool applyAlphaToDiffuse = false) {
	//金属度越强，反射越强，即漫反射越少
	float oneMinusReflectivity = OneMinusReflectivity(surface.metallic);
	BRDF brdf;
	brdf.diffuse = surface.color * oneMinusReflectivity;
	if(applyAlphaToDiffuse)	{
		brdf.diffuse *= surface.alpha;
	}
	brdf.specular = lerp(MIN_REFLECTIVITY, surface.color, surface.metallic);
	float perceptualRoughness =
		PerceptualSmoothnessToPerceptualRoughness(surface.smoothness);
	brdf.roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	return brdf;
}

#endif