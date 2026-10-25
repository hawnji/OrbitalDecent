// Orbital Descent - ocean surface: procedural wave normals, Fresnel sky reflection and sun glint.
Shader "OrbitalDescent/Water"
{
    Properties
    {
        _DeepColor ("Deep colour", Color) = (0.01, 0.03, 0.05, 1)
        _Time2 ("Simulation time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "ODCommon.cginc"

            float4 _DeepColor;
            float _Time2;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 sun : TEXCOORD1;
                float3 amb : TEXCOORD2;
                SHADOW_COORDS(3)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 lp = ODLocal(o.worldPos);
                o.sun = ODSunLight(lp);
                o.amb = ODAmbient(lp);
                TRANSFER_SHADOW(o);
                return o;
            }

            // Sum of directional sine waves (slopes only); amplitude fades with distance to avoid aliasing.
            float2 WaveSlope(float2 p, float t, float fade)
            {
                float2 s = 0;
                float2 dirs[4] = { float2(0.8, 0.6), float2(-0.3, 0.95), float2(0.95, -0.3), float2(-0.7, -0.7) };
                float lens[4] = { 60.0, 23.0, 9.0, 3.7 };
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float w = 2.0 * OD_PI / lens[k];
                    float c = sqrt(9.81 / w);
                    float ph = dot(dirs[k], p) * w - c * w * t;
                    float a = 0.035 * lens[k] / 6.2832 * fade;
                    s += dirs[k] * a * w * cos(ph);
                }
                return s;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 lp = ODLocal(i.worldPos);
                float dist = length(i.worldPos - _WorldSpaceCameraPos);
                float fade = saturate(1.0 - dist / 6000.0);
                float2 slope = WaveSlope(lp.xz, _Time2, fade);
                float3 up = UpLocal(lp);
                float3 n = normalize(up - float3(slope.x, 0, slope.y));
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, v)), 5.0);
                float3 skyRefl = i.amb * 2.2;
                float shadow = SHADOW_ATTENUATION(i) * ODCloudShadow(lp);
                float3 h = normalize(_SunDir + v);
                float rough = lerp(0.08, 0.25, 1.0 - fade);
                float glint = ODSpecular(n, v, _SunDir, rough);
                float3 body = _DeepColor.rgb * (i.sun * saturate(dot(up, _SunDir)) / OD_PI + i.amb);
                float3 c = lerp(body, skyRefl, fres) + i.sun * glint * 0.02 * shadow;
                return float4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
