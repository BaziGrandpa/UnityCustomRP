#ifndef CUSTOM_SHADOWS_INCLUDED
#define CUSTOM_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

#if defined(_DIRECTIONAL_PCF3)
	#define DIRECTIONAL_FILTER_SAMPLES 4
	#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#elif defined(_DIRECTIONAL_PCF5)
	#define DIRECTIONAL_FILTER_SAMPLES 9
	#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#elif defined(_DIRECTIONAL_PCF7)
	#define DIRECTIONAL_FILTER_SAMPLES 16
	#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#endif

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_CASCADE_COUNT 4

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);//声明这个ID是shadowmap所有
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);//这个采样器包含了当前深度与目标的比对

CBUFFER_START(_CustomShadows)
	int _CascadeCount;
	float4 _CascadeCullingSpheres[MAX_CASCADE_COUNT];
	float4 _CascadeData[MAX_CASCADE_COUNT];
	float4x4 _DirectionalShadowMatrices[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT*MAX_CASCADE_COUNT];
	float4 _ShadowAtlasSize;
	float4 _ShadowDistanceFade;
CBUFFER_END

	struct DirectionalShadowData
    {
        float strength;
        int tileIndex;
		float normalBias;
    };

	struct ShadowData {
		float cascadeBlend;//处理不同级联的混合
		int cascadeIndex;
		float strength;//用于处理超出级联范围的cascadeIndex
	};

float FadedShadowStrength (float distance, float scale, float fade) {
	return saturate((1.0 - distance * scale) * fade);
}

	//确定在第几级联采样
	ShadowData GetShadowData (Surface surfaceWS) {
		ShadowData data;
		data.strength = FadedShadowStrength(surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y);
		data.cascadeBlend = 1.0;
		int i;
		for (i = 0; i < _CascadeCount; i++) {
			float4 sphere = _CascadeCullingSpheres[i];
			float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
			//找到所属的级联
			if (distanceSqr < sphere.w) {
				float fade = FadedShadowStrength(
								distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z
							);
				//最后一级不用混合
				if (i == _CascadeCount - 1) {
				data.strength *= fade;
				}
				else {
					data.cascadeBlend = fade;
				}
				break;
			}
		}

		if( i==_CascadeCount ){
		data.strength = 0.0;
		}
		#if defined(_CASCADE_BLEND_DITHER)
		else if (data.cascadeBlend < surfaceWS.dither) {
			i += 1;
		}
		#endif
		#if !defined(_CASCADE_BLEND_SOFT)
		data.cascadeBlend = 1.0;
		#endif
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

//PCF采样
float FilterDirectionalShadow (float3 positionSTS) {
	#if defined(DIRECTIONAL_FILTER_SETUP)
		float weights[DIRECTIONAL_FILTER_SAMPLES];
		float2 positions[DIRECTIONAL_FILTER_SAMPLES];
		float4 size = _ShadowAtlasSize.yyxx;
		DIRECTIONAL_FILTER_SETUP(size, positionSTS.xy, weights, positions);
		float shadow = 0;
		for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++) {
			shadow += weights[i] * SampleDirectionalShadowAtlas(
				float3(positions[i].xy, positionSTS.z)
			);
		}
		return shadow;
	#else
		return SampleDirectionalShadowAtlas(positionSTS);
	#endif
}

//直接用给出片元，采样出sm
float GetDirectionalShadowAttenuation (DirectionalShadowData  directional, ShadowData global, Surface surfaceWS) {
	#if !defined(_RECEIVE_SHADOWS)
		return 1.0;
	#endif
	if (directional.strength <= 0.0) {
		return 1.0;
	}
	//沿着法线方向缩进一点
	float3 normalBias = surfaceWS.normal *
		(directional.normalBias * _CascadeData[global.cascadeIndex].y);
	float3 positionSTS = mul(
		_DirectionalShadowMatrices[directional.tileIndex],
		float4(surfaceWS.position + normalBias, 1.0)
	).xyz;//变换到shadommap图集空间下，这里已经可以去到正确的级联了
	float shadow = FilterDirectionalShadow(positionSTS);//
	//blend级联，关键在于cascadeID+1
	if (global.cascadeBlend < 1.0) {
			normalBias = surfaceWS.normal *
				(directional.normalBias * _CascadeData[global.cascadeIndex + 1].y);
			positionSTS = mul(
				_DirectionalShadowMatrices[directional.tileIndex + 1],
				float4(surfaceWS.position + normalBias, 1.0)
			).xyz;
			shadow = lerp(
				FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend
			);
		}
	return lerp(1.0, shadow, directional.strength);
}

#endif