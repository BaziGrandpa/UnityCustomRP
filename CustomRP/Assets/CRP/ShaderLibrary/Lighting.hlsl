#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED
//处理光照计算的
//计算这个点可以辐射多少能量！即计算了这个点总体输入的能量
float3 IncomingLight (Surface surface, Light light) {
	return saturate(dot(surface.normal, light.direction)*light.attenuation) * light.color;
}

//surface包含了观察方向，以及该点的数据，高光+漫反射
float3 DirectBRDF (Surface surface, BRDF brdf, Light light) {
	return SpecularStrength(surface, brdf, light) * brdf.specular + brdf.diffuse;
}

//用这个像素接受到的总体能量*经过brdf计算后再有多少可以反射出来
float3 GetLighting (Surface surface, BRDF brdf, Light light) {
	return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

//最多支持四盏灯
float3 GetLighting (Surface surfaceWS, BRDF brdf, GI gi) {
	ShadowData shadowData = GetShadowData(surfaceWS);
	shadowData.shadowMask = gi.shadowMask;
	//return gi.shadowMask.shadows.rgb;先不玩shadowmask了
	float3 color = gi.diffuse;
	for (int i = 0; i < GetDirectionalLightCount(); i++) {
		Light light = GetDirectionalLight(i, surfaceWS, shadowData);
		color += GetLighting(surfaceWS, brdf, light);//算各盏灯的贡献，然后累加			
	}
	return color;
}

#endif
