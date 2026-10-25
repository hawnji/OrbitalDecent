// Orbital Descent - full-screen atmosphere and cloud composition (built-in pipeline image effect).
// Pass 0: volumetric clouds into a low-resolution buffer (rgb = light, a = transmittance).
// Pass 1: full-resolution composition of scene, far planet, sky, aerial perspective and clouds,
//         followed by exposure and ACES tonemapping.
Shader "Hidden/OrbitalDescent/Atmosphere"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "ODCommon.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D_float _CameraDepthTexture;
        sampler2D _CloudTex;
        sampler2D _DitherTex;
        float4 _DitherTex_TexelSize;

        float3 _CamRight;
        float3 _CamUpV;
        float3 _CamFwd;
        float _TanHalfFov;
        float _Aspect;
        float4 _FadeColor;   // rgb colour, a amount (transitions, blackout)
        float _Vignette;

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;      // samples _MainTex and intermediate buffers
            float2 uvScreen : TEXCOORD1; // screen-space uv (bottom-up), for depth and view rays
        };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            o.uvScreen = v.texcoord;
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0) o.uvScreen.y = 1.0 - o.uvScreen.y;
            #endif
            return o;
        }

        float3 ViewDir(float2 uvScreen)
        {
            float2 ndc = uvScreen * 2.0 - 1.0;
            return normalize(_CamFwd + _CamRight * ndc.x * _TanHalfFov * _Aspect + _CamUpV * ndc.y * _TanHalfFov);
        }

        // Distance along the view ray to rasterised geometry; hasScene = 0 for empty (sky) pixels.
        float SceneDistance(float2 uvScreen, float3 dir, out float hasScene)
        {
            float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uvScreen);
            float lin01 = Linear01Depth(raw);
            hasScene = lin01 < 0.9999 ? 1.0 : 0.0;
            return LinearEyeDepth(raw) / max(1e-4, dot(dir, _CamFwd));
        }

        float4 fragClouds(v2f i) : SV_Target
        {
            float3 dir = ViewDir(i.uvScreen);
            float hasScene;
            float dist = SceneDistance(i.uvScreen, dir, hasScene);
            float tMax;
            if (hasScene > 0.5) tMax = dist;
            else
            {
                float2 g = RaySphereCam(dir, 0.0);
                tMax = g.x > 0.0 ? g.x : 1e9;
            }
            float2 dpx = i.pos.xy * _DitherTex_TexelSize.xy;   // pixel coordinates -> tiled dither
            float jitter = frac(tex2Dlod(_DitherTex, float4(dpx, 0, 0)).r + _FrameJitter);
            return CloudPass(dir, tMax, jitter);
        }

        float4 fragComposite(v2f i) : SV_Target
        {
            float3 dir = ViewDir(i.uvScreen);
            float hasScene;
            float dist = SceneDistance(i.uvScreen, dir, hasScene);
            float3 scene = tex2D(_MainTex, i.uv).rgb;
            float4 cloud = tex2D(_CloudTex, i.uv);
            float3 col = CompositeView(dir, dist, hasScene, scene, cloud);
            col = TonemapACES(col);
            float2 c = i.uvScreen - 0.5;
            col *= 1.0 - _Vignette * dot(c, c) * 1.6;
            col = lerp(col, _FadeColor.rgb, _FadeColor.a);
            return float4(col, 1.0);
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragClouds
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            #pragma target 3.5
            ENDCG
        }
    }
    Fallback Off
}
