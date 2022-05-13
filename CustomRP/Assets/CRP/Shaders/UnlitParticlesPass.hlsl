#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED
#include "../ShaderLibrary/Common.hlsl"


TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

//GPU Instancing
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
	UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
	UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
	UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

struct Attributes {
	float3 positionOS : POSITION;
	float4 color : COLOR;
	float2 baseUV : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct Varyings {
	float4 positionCS : SV_POSITION;
	#if defined(_VERTEX_COLORS)
		float4 color : VAR_COLOR;
	#endif
	float2 baseUV : VAR_BASE_UV;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings UnlitPassVertex (Attributes input) {
	Varyings output;
	UNITY_SETUP_INSTANCE_ID(input);//这个意思是是让宏获取当前
	UNITY_TRANSFER_INSTANCE_ID(input, output);//将这个ID拷贝到output
	float3 positionWS = TransformObjectToWorld(input.positionOS);//WS for WorldSpace
	output.positionCS = TransformWorldToHClip(positionWS);//mvp变换

	float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseMap_ST);
	output.baseUV = input.baseUV * baseST.xy + baseST.zw;

	#if defined(_VERTEX_COLORS)
		output.color = input.color;		
	#endif
	return output;
}

float4 UnlitPassFragment (Varyings input):SV_TARGET{
	UNITY_SETUP_INSTANCE_ID(input);
	float2 fixed_uv = input.baseUV/4.f;
	float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, fixed_uv);
	float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
	float4 base =  baseMap * baseColor;//这个不是点乘，而是各分量相乘，效果是贴图叠加一层颜色
	#if defined(_CLIPPING)
		clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));//alpha小于阈值的直接丢弃
	#endif
	#if defined(_VERTEX_COLORS)
		base = base * input.color;
	#endif
	return base;
}

#endif

