// Orbital Descent - near-field terrain. Uses the same albedo function as the analytic far planet
// in the atmosphere pass so the patch blends into the horizon without a seam.
Shader "OrbitalDescent/Terrain"
{
    Properties
    {
        _ZoneElevation ("Zone elevation", Float) = 0
        _SnowLine ("Snow line", Float) = 2200
        _Ocean ("Ocean level", Float) = 0
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

            float _ZoneElevation, _SnowLine, _Ocean;
            float4 _ProbeShadow;
            float _ProbeShadowStrength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 horiz : TEXCOORD2;    // local horizontal position (m)
                float2 hs : TEXCOORD3;       // height ASL (m), slope
                float3 sun : TEXCOORD4;
                float3 amb : TEXCOORD5;
                SHADOW_COORDS(6)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.horiz = v.uv * 1000.0;
                o.hs = v.uv2;
                float3 lp = ODLocal(o.worldPos);
                o.sun = ODSunLight(lp);
                o.amb = ODAmbient(lp);
                TRANSFER_SHADOW(o);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // On ocean levels the sea floor is hidden under the water surface.
                if (_Ocean > 0.5 && i.hs.x < -1.0) discard;
                float3 n = normalize(i.normal);
                // GroundAlbedo puts the snow line at 2200 m; shift the height so this level's line applies.
                float3 albedo = GroundAlbedo(i.horiz, i.hs.x - (_SnowLine - 2200.0), i.hs.y, 0.0);
                // Wet beach band near sea level on ocean levels.
                if (_Ocean > 0.5) albedo = lerp(albedo, float3(0.32, 0.29, 0.22), saturate(1.0 - abs(i.hs.x - 2.0) / 6.0));
                float shadow = SHADOW_ATTENUATION(i);
                if (_ProbeShadow.w > 0.0)
                {
                    float d = length(i.worldPos - _ProbeShadow.xyz) / _ProbeShadow.w;
                    shadow *= 1.0 - _ProbeShadowStrength * saturate(1.0 - d * d);
                }
                float3 lp = ODLocal(i.worldPos);
                shadow *= ODCloudShadow(lp);
                float nl = saturate(dot(n, _SunDir));
                float3 c = albedo * (i.sun * nl * shadow / OD_PI + i.amb);
                return float4(c, 1.0);
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
