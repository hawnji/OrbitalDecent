// Preview entry point: renders sky, planet, atmosphere and clouds (no rasterised scene) so the shared
// HLSL core can be inspected in a browser. Compiled by build.sh with glslang + SPIRV-Cross.
#define OD_TEX2D(n) Texture2D n; SamplerState sampler##n;
#define OD_TEX3D(n) Texture3D n; SamplerState sampler##n;
#define OD_SAMPLE2D(n, uv) n.SampleLevel(sampler##n, uv, 0)
#define OD_SAMPLE3D(n, uvw) n.SampleLevel(sampler##n, uvw, 0)
#include "../../Assets/Resources/Shaders/ODAtmosphereCore.hlsl"

float2 _Resolution;
float3 _CamRight;
float3 _CamUpV;
float3 _CamFwd;
float _TanHalfFov;
float _Debug;   // 0 normal, 1 ground distance, 2 atmosphere only, 3 cloud transmittance, 4 far ground colour

float4 main(float4 pos : SV_Position) : SV_Target
{
    float2 uv = pos.xy / _Resolution;
    float2 ndc = uv * 2.0 - 1.0;
    float aspect = _Resolution.x / _Resolution.y;
    float3 dir = normalize(_CamFwd + _CamRight * ndc.x * _TanHalfFov * aspect + _CamUpV * ndc.y * _TanHalfFov);
    float2 g = RaySphereCam(dir, 0.0);
    float tMax = g.x > 0.0 ? g.x : 1e9;
    float jitter = frac(sin(dot(pos.xy, float2(12.9898, 78.233))) * 43758.5453);
    if (_Debug > 0.5 && _Debug < 1.5) return float4(g.x > 0.0 ? g.x / 1e6 : 0.0, g.x > 0.0 ? 0.0 : 1.0, isnan(g.x) ? 1.0 : 0.0, 1);
    if (_Debug > 1.5 && _Debug < 2.5) { AtmoResult a = IntegrateAtmosphere(dir, tMax, _AtmoSteps); float3 v = a.inscatter; return float4(isnan(v.x) ? 1.0 : saturate(v.x), saturate(a.transmittance.y), isnan(a.transmittance.x) ? 1.0 : 0.0, 1); }
    if (_Debug > 3.5 && _Debug < 4.5) { float3 v = g.x > 0.0 ? ShadeFarGround(_CamLocal + dir * g.x, dir) : float3(0, 0, 0); return float4(isnan(v.x) ? 1.0 : saturate(v.x * 5.0), saturate(v.y * 5.0), isnan(v.y) ? 1.0 : 0.0, 1); }
    float4 cloud = CloudPass(dir, tMax, jitter);
    if (_Debug > 4.5) return float4(cloud.rgb * (_Debug - 4.0) * 0.1, 1);
    if (_Debug > 2.5) return float4(isnan(cloud.a) ? 1.0 : cloud.a, isnan(cloud.r) ? 1.0 : 0.0, 0, 1);
    float3 col = CompositeView(dir, 0.0, 0.0, float3(0, 0, 0), cloud);
    col = TonemapACES(col);
    col = pow(col, 1.0 / 2.2);
    return float4(col, 1.0);
}
