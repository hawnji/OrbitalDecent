// Orbital Descent - shared include for Unity shaders (built-in render pipeline).
// Binds the engine-agnostic atmosphere core to Unity's sampler syntax and adds helpers used by the
// scene shaders (sun light at an altitude, ambient, cloud shadow, local-frame positions).
#ifndef OD_COMMON_INCLUDED
#define OD_COMMON_INCLUDED

#define OD_TEX2D(n) sampler2D n;
#define OD_TEX3D(n) sampler3D n;
#define OD_SAMPLE2D(n, uv) tex2Dlod(n, float4((uv), 0, 0))
#define OD_SAMPLE3D(n, uvw) tex3Dlod(n, float4((uvw), 0))

#include "ODAtmosphereCore.hlsl"

float3 _ODOrigin;            // floating origin in the landing-zone frame (m)
float _ODCloudShadows;       // 1 when cloud shadows are enabled for this quality tier
float _ODFlashAmbient;       // lightning flash ambient boost

// Unity world position (relative to the floating origin) to the landing-zone frame.
float3 ODLocal(float3 worldPos) { return worldPos + _ODOrigin; }

// Direct sun radiance arriving at a point, attenuated by the atmosphere.
float3 ODSunLight(float3 localPos)
{
    float alt = AltitudeLocal(localPos);
    return SunTransmittance(max(alt, 0.0), dot(UpLocal(localPos), _SunDir)) * _SunIntensity;
}

// Sky light plus lightning flash.
float3 ODAmbient(float3 localPos)
{
    return SkyAmbient(AltitudeLocal(localPos)) + float3(0.75, 0.8, 1.0) * _ODFlashAmbient;
}

float ODCloudShadow(float3 localPos)
{
    return _ODCloudShadows > 0.5 ? CloudShadow(localPos) : 1.0;
}

// Small GGX specular lobe, normalised so a rough surface stays dim.
float ODSpecular(float3 n, float3 v, float3 l, float roughness)
{
    float3 h = normalize(l + v);
    float a = max(0.03, roughness * roughness);
    float a2 = a * a;
    float nh = saturate(dot(n, h));
    float d = nh * nh * (a2 - 1.0) + 1.0;
    float D = a2 / (OD_PI * d * d);
    float nl = saturate(dot(n, l));
    float nv = saturate(dot(n, v)) + 1e-3;
    float k = a * 0.5;
    float G = (nl / (nl * (1.0 - k) + k)) * (nv / (nv * (1.0 - k) + k));
    return D * G / (4.0 * nl * nv + 1e-3) * nl;
}

#endif
