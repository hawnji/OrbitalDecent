// Minimal stand-ins for Unity's built-in include, enough to type-check Orbital Descent shaders
// with glslang outside the editor. Not used by Unity.
#ifndef UNITY_CG_STUB
#define UNITY_CG_STUB
float4x4 unity_ObjectToWorld;
float4x4 unity_MatrixVP;
float3 _WorldSpaceCameraPos;
float4 _ZBufferParams;
float4 _Time;
#define UNITY_UV_STARTS_AT_TOP 1
#define sampler2D_float sampler2D
struct appdata_img { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
struct appdata_base { float4 vertex : POSITION; float3 normal : NORMAL; float4 texcoord : TEXCOORD0; };
struct appdata_full { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float4 texcoord : TEXCOORD0; float4 texcoord1 : TEXCOORD1; float4 color : COLOR; };
float4 UnityObjectToClipPos(float4 v) { return mul(unity_MatrixVP, mul(unity_ObjectToWorld, v)); }
float4 UnityObjectToClipPos(float3 v) { return UnityObjectToClipPos(float4(v, 1)); }
float3 UnityObjectToWorldNormal(float3 n) { return normalize(mul((float3x3)unity_ObjectToWorld, n)); }
#define SAMPLE_DEPTH_TEXTURE(s, uv) (tex2D(s, uv).r)
float Linear01Depth(float z) { return 1.0 / (_ZBufferParams.x * z + _ZBufferParams.y); }
float LinearEyeDepth(float z) { return 1.0 / (_ZBufferParams.z * z + _ZBufferParams.w); }
#define V2F_SHADOW_CASTER float4 pos : SV_POSITION
#define TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) o.pos = UnityObjectToClipPos(v.vertex);
#define SHADOW_CASTER_FRAGMENT(i) return 0;
#endif
