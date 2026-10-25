// Orbital Descent - additive glow for plasma, engine plume, trail, beacon and lightning.
// Brightness = _Color * _Intensity * vertex colour; vertex alpha and uv.y give soft falloff.
Shader "OrbitalDescent/Fx"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Softness ("Edge softness", Range(0, 1)) = 0.5
        _FadeLength ("Fade length along uv.y (0 = off)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            float4 _Color;
            float _Intensity, _Softness, _FadeLength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float rim : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 vd = normalize(_WorldSpaceCameraPos - wp);
                // Fade surfaces seen edge-on so volumes (sheath, plume) read as glowing gas, not shells.
                o.rim = dot(n, n) > 0.01 ? abs(dot(normalize(n), vd)) : 1.0;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float edge = lerp(1.0, smoothstep(0.0, 1.0, i.rim), _Softness);
                float along = _FadeLength > 0.0 ? saturate(1.0 - i.uv.y / _FadeLength) : 1.0;
                float3 c = _Color.rgb * _Intensity * i.color.rgb * i.color.a * edge * along * along;
                return float4(c, 0);
            }
            ENDCG
        }
    }
    Fallback Off
}
