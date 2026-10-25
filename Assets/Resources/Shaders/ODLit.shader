// Orbital Descent - lit opaque surfaces (probe, pad, ship). Sun light is attenuated by the same
// atmosphere model as the sky, so objects redden and dim consistently with altitude.
Shader "OrbitalDescent/Lit"
{
    Properties
    {
        _Color ("Albedo", Color) = (0.7, 0.7, 0.7, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Roughness ("Roughness", Range(0.05, 1)) = 0.6
        [HDR] _Emission ("Emission", Color) = (0, 0, 0, 1)
        _ProbeShadow ("Blob shadow (xyz, radius)", Vector) = (0, 0, 0, 0)
        _ProbeShadowStrength ("Blob shadow strength", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

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

            float4 _Color;
            float _Metallic, _Roughness;
            float4 _Emission;
            float4 _ProbeShadow;
            float _ProbeShadowStrength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float4 color : COLOR;
                float3 sun : TEXCOORD2;
                float3 amb : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                float3 lp = ODLocal(o.worldPos);
                o.sun = ODSunLight(lp);
                o.amb = ODAmbient(lp);
                TRANSFER_SHADOW(o);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 l = _SunDir;
                float3 albedo = _Color.rgb * i.color.rgb;
                float shadow = SHADOW_ATTENUATION(i);
                if (_ProbeShadow.w > 0.0)
                {
                    float d = length(i.worldPos - _ProbeShadow.xyz) / _ProbeShadow.w;
                    shadow *= 1.0 - _ProbeShadowStrength * saturate(1.0 - d * d);
                }
                shadow *= ODCloudShadow(ODLocal(i.worldPos));
                float nl = saturate(dot(n, l));
                float3 diffuse = albedo * (1.0 - _Metallic) / OD_PI;
                float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, _Metallic);
                float spec = ODSpecular(n, v, l, _Roughness);
                float3 direct = i.sun * shadow * (diffuse * nl + f0 * spec);
                // Hemisphere ambient: sky from above, ground bounce from below.
                float hemi = dot(n, UpLocal(ODLocal(i.worldPos))) * 0.5 + 0.5;
                float3 ambient = albedo * i.amb * lerp(0.35, 1.0, hemi);
                return float4(direct + ambient + _Emission.rgb, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct v2f { V2F_SHADOW_CASTER; };
            v2f vert(appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) return o; }
            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback Off
}
