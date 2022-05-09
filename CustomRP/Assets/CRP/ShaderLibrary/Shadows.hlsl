#ifndef CUSTOM_SHADOWS_INCLUDED
#define CUSTOM_SHADOWS_INCLUDED

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_CASCADE_COUNT 4

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);//声明这个ID是shadowmap所有
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);//这个采样器包含了当前深度与目标的比对

CBUFFER_START(_CustomShadows)
	int _CascadeCount;
	float4 _CascadeCullingSpheres[MAX_CASCADE_COUNT];
	float4x4 _DirectionalShadowMatrices[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT*MAX_CASCADE_COUNT];
	float _ShadowDistance;
CBUFFER_END

	struct DirectionalShadowData
    {
        float strength;
        int tileIndex;
    };

	struct ShadowData {
		int cascadeIndex;
		float strength;//用于处理超出级联范围的cascadeIndex
	};
	//确定在第几级联采样
	ShadowData GetShadowData (Surface surfaceWS) {
		ShadowData data;
		data.strength =  surfaceWS.depth < _ShadowDistance ? 1.0 : 0.0;
		int i;
		for (i = 0; i < _CascadeCount; i++) {
			float4 sphere = _CascadeCullingSpheres[i];
			float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
			if (distanceSqr < sphere.w) {
				break;
			}
		}

		if( i==_CascadeCount ){
		data.strength = 0.0;
		}
			
		data.cascadeIndex = i;
		return data;
	}

//给定从深度图采样
float SampleDirectionalShadowAtlas (float3 positionSTS) {
	return SAMPLE_TEXTURE2D_SHADOW(
		_DirectionalShadowAtlas, SHADOW_SAMPLER, positionSTS
	);
//这个采样接口返回的是处理后的值了，如果这个点完全在阴影里返回0，完全不在阴影则返回1
}

//直接用给出片元，采样出sm
float GetDirectionalShadowAttenuation (DirectionalShadowData data, Surface surfaceWS) {
	if (data.strength <= 0.0) {
		return 1.0;
	}
	float3 positionSTS = mul(
		_DirectionalShadowMatrices[data.tileIndex],
		float4(surfaceWS.position, 1.0)
	).xyz;//变换到shadommap图集空间下
	float shadow = SampleDirectionalShadowAtlas(positionSTS);//
	return lerp(1.0, shadow, data.strength);
}

#endif