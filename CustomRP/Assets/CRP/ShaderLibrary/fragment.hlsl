#ifndef FRAGMENT_INCLUDED
#define FRAGMENT_INCLUDED

struct Fragment {
	float2 positionSS;
	float depth;
};

Fragment GetFragment (float4 positionSS) {
	Fragment f;
	f.positionSS = positionSS.xy;
	f.depth = positionSS.w;//这是观测空间的depth，是距离摄像机的深度而非近平面
	return f;
}

#endif