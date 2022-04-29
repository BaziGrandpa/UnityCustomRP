#ifndef CUSTOM_LIT_PASS_INCLUDED
#define CUSTOM_LIT_PASS_INCLUDED
#include "../ShaderLibrary/Common.hlsl"
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Shadows.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"
#include "../ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
	UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
	UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
	UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
	UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
	UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

struct Attributes {
	float3 positionOS : POSITION;
	float3 normalOS : NORMAL;
	float2 baseUV : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct Varyings {
	float4 positionCS : SV_POSITION;
	float3 positionWS : VAR_POSITION;
	float3 normalWS : VAR_NORMAL;
	float2 baseUV : VAR_BASE_UV;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings LitPassVertex  (Attributes input) {
	Varyings output;
	UNITY_SETUP_INSTANCE_ID(input);//这个意思是是让宏获取当前
	UNITY_TRANSFER_INSTANCE_ID(input, output);//将这个ID拷贝到output
	output.positionWS = TransformObjectToWorld(input.positionOS);//WS for WorldSpace
	output.positionCS = TransformWorldToHClip(output.positionWS);//mvp变换

	float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
	output.baseUV = input.baseUV * baseST.xy + baseST.zw;

	output.normalWS = TransformObjectToWorldNormal(input.normalOS);

	return output;
}

float4 LitPassFragment (Varyings input):SV_TARGET{
	UNITY_SETUP_INSTANCE_ID(input);
	float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV);
	float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
	float4 base =  baseMap * baseColor;//这个不是点乘，而是各分量相乘
	#if defined(_CLIPPING)
		clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));//小于阈值的直接丢弃
	#endif
	Surface surface;
	surface.position = input.positionWS;
	surface.normal = normalize(input.normalWS);
	surface.color = base.rgb;
	surface.alpha = base.a;
	surface.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
	surface.smoothness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Smoothness);
	surface.viewDirection = normalize(_WorldSpaceCameraPos - input.positionWS);

	#if defined(_PREMULTIPLY_ALPHA)
		BRDF brdf = GetBRDF(surface, true);
	#else
		BRDF brdf = GetBRDF(surface);
	#endif
	
	float3 color = GetLighting(surface, brdf);
	
	return float4(color, surface.alpha);//rgb和alpha分开算了
}

#endif

