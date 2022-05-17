#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED
#include "../ShaderLibrary/Common.hlsl"
#define INPUT_PROP(name) UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, name)

TEXTURE2D(_CameraColorTexture);
TEXTURE2D(_CameraDepthTexture);
TEXTURE2D(_BaseMap);
TEXTURE2D(_DistortionMap);
SAMPLER(sampler_DistortionMap);
SAMPLER(sampler_BaseMap);

//GPU Instancing
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
	UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
	UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
	UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
	UNITY_DEFINE_INSTANCED_PROP(float, _NearFadeDistance)
	UNITY_DEFINE_INSTANCED_PROP(float, _NearFadeRange)
	UNITY_DEFINE_INSTANCED_PROP(float, _SoftParticlesDistance)
	UNITY_DEFINE_INSTANCED_PROP(float, _SoftParticlesRange)
	UNITY_DEFINE_INSTANCED_PROP(float, _DistortionStrength)
	UNITY_DEFINE_INSTANCED_PROP(float, _DistortionBlend)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

float2 TransformBaseUV (float2 baseUV) {
	float4 baseST = INPUT_PROP(_BaseMap_ST);
	return baseUV * baseST.xy + baseST.zw;
}

//采样colorbuffer，支持偏移
float4 GetBufferColor (float2 screenUV, float2 uvOffset = float2(0.0, 0.0)) {
	float2 uv = screenUV + uvOffset;
	return SAMPLE_TEXTURE2D(_CameraColorTexture, sampler_point_mirror, uv);
}

float2 GetDistortion (float2 baseUV ,float3 flipbookUVB = float3(0.0,0.0,0.0) ) {
	float4 rawMap = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, baseUV);
	#if defined(_FLIPBOOK_BLENDING)
	rawMap = lerp(
		rawMap, SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, flipbookUVB.xy),
		flipbookUVB.z
	);
	#endif
	return DecodeNormal(rawMap, INPUT_PROP(_DistortionStrength)).xy;
}

struct Attributes {
	float3 positionOS : POSITION;
	float4 color : COLOR;
	#if defined(_FLIPBOOK_BLENDING)
	float4 baseUV : TEXCOORD0;
	float flipbookBlend : TEXCOORD1;
	#else
	float2 baseUV : TEXCOORD0;
	#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct Varyings {
	float4 positionCS_SS : SV_POSITION;
	#if defined(_VERTEX_COLORS)
	float4 color : VAR_COLOR;
	#endif
	#if defined(_FLIPBOOK_BLENDING)
	float3 flipbookUVB : VAR_FLIPBOOK;
	#endif
	float2 baseUV : VAR_BASE_UV;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings UnlitPassVertex (Attributes input) {
	Varyings output;
	UNITY_SETUP_INSTANCE_ID(input);//这个意思是是让宏获取当前
	UNITY_TRANSFER_INSTANCE_ID(input, output);//将这个ID拷贝到output
	float3 positionWS = TransformObjectToWorld(input.positionOS);//WS for WorldSpace
	output.positionCS_SS = TransformWorldToHClip(positionWS);//变换到屏幕空间了

	output.baseUV.xy = TransformBaseUV(input.baseUV.xy);
	#if defined(_FLIPBOOK_BLENDING)
		output.flipbookUVB.xy = TransformBaseUV(input.baseUV.zw);
		output.flipbookUVB.z = input.flipbookBlend;
	#endif

	#if defined(_VERTEX_COLORS)
		output.color = input.color;		
	#endif
	return output;
}

float4 UnlitPassFragment (Varyings input):SV_TARGET{
	UNITY_SETUP_INSTANCE_ID(input);
	float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV);
	Fragment frag = GetFragment(input.positionCS_SS);
	
	#if defined _FLIPBOOK_BLENDING
	//开启帧动画混合，则用uv0采样当前帧，uv1采样下一帧，混合后得到
		baseMap = lerp(
			baseMap, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.flipbookUVB.xy),
			input.flipbookUVB.z
		);
	#endif
	
	#if defined(_NEAR_FADE)
	//靠近摄像机的fade效果
		float nearAttenuation = (frag.depth - INPUT_PROP(_NearFadeDistance)) / INPUT_PROP(_NearFadeRange);
		baseMap.a *= saturate(nearAttenuation);
	#endif

	
	//采样屏幕深度buffer
	float2 screenUV = frag.positionSS / _ScreenParams.xy;//获取到屏幕空间的uv
	//采样原来所用的深度
	//float bufferDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, screenUV, 0);
	float bufferDepth = LOAD_TEXTURE2D(_CameraDepthTexture, frag.positionSS).r;
	bufferDepth = LinearEyeDepth(bufferDepth, _ZBufferParams);//
	float depthDelta = bufferDepth - frag.depth;
	float d_nearAttenuation = (depthDelta - INPUT_PROP(_SoftParticlesDistance)) / INPUT_PROP(_SoftParticlesRange) ;
	baseMap.a *= saturate(d_nearAttenuation);

	float2 distortion = GetDistortion(input.baseUV.xy) * baseMap.a;; 
	//float4 bufferColor = GetBufferColor(screenUV,distortion);

	

	float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);	
	float4 base =  baseMap * baseColor;//这个不是点乘，而是各分量相乘，效果是贴图叠加一层颜色

	#if defined(_DISTORTION)
	base.rgb = lerp(
			GetBufferColor(screenUV, distortion).rgb, base.rgb,
			saturate(base.a - INPUT_PROP(_DistortionBlend))
		);
	#endif

	#if defined(_VERTEX_COLORS)
		base = base * input.color;
	#endif
	
	
		
	#if defined(_CLIPPING)
		clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));//alpha小于阈值的直接丢弃
	#endif
	return base;
	//return bufferColor;
	
}

#endif

